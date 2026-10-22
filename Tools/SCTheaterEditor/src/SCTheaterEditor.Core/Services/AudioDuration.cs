using System.Buffers.Binary;
using System.IO;

namespace SCTheaterEditor.Services;

/// <summary>
/// 추가(6회차): 오디오 파일 길이(초) — 파일 머리만 읽음 (재생 라이브러리 없이 목록 표시용).
/// - wav: RIFF의 fmt(초당 바이트) · data(크기) 청크
/// - mp3: ID3v2 태그를 건너뛴 첫 프레임 → Xing/Info 또는 VBRI 헤더의 프레임 수, 없으면 고정 비트레이트로 계산
/// - 그 밖·읽지 못하면 null
/// </summary>
public static class AudioDuration
{
    public static double? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".wav" => Wav(fs),
                ".mp3" => Mp3(fs),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ wav

    internal static double? Wav(Stream s)
    {
        var head = new byte[12];
        if (s.Read(head) < 12 || !Tag(head, 0, "RIFF") || !Tag(head, 8, "WAVE")) return null;
        uint byteRate = 0;
        var chunk = new byte[8];
        while (s.Read(chunk) == 8)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            if (Tag(chunk, 0, "fmt "))
            {
                var fmt = new byte[Math.Min(size, 16u)];
                if (s.Read(fmt) < 12) return null;
                byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(8));
                s.Seek(size - fmt.Length + (size & 1), SeekOrigin.Current);
            }
            else if (Tag(chunk, 0, "data"))
            {
                if (byteRate == 0) return null;
                long dataSize = Math.Min(size, s.Length - s.Position);   // 크기가 잘못 적힌 파일 대비
                return (double)dataSize / byteRate;
            }
            else s.Seek(size + (size & 1), SeekOrigin.Current);   // 청크는 짝수 바이트로 맞춤
        }
        return null;
    }

    // ------------------------------------------------------------------ mp3

    private static readonly int[] BitrateV1L3 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
    private static readonly int[] BitrateV2L3 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];
    private static readonly int[] SampleRateV1 = [44100, 48000, 32000, 0];

    internal static double? Mp3(Stream s)
    {
        long start = 0;
        var id3 = new byte[10];
        if (s.Read(id3) == 10 && Tag(id3, 0, "ID3"))
        {
            // ID3v2 크기 = 4바이트 7비트씩(syncsafe), 플래그 0x10이면 바닥글 10바이트 더
            long size = (id3[6] & 0x7F) << 21 | (id3[7] & 0x7F) << 14 | (id3[8] & 0x7F) << 7 | (id3[9] & 0x7F);
            start = 10 + size + ((id3[5] & 0x10) != 0 ? 10 : 0);
        }

        // 첫 프레임 찾기 (앞 64KB 안)
        s.Position = start;
        var buf = new byte[64 * 1024];
        int n = s.Read(buf);
        for (int i = 0; i + 4 <= n; i++)
        {
            if (buf[i] != 0xFF || (buf[i + 1] & 0xE0) != 0xE0) continue;
            int version = (buf[i + 1] >> 3) & 3;   // 3 = MPEG1, 2 = MPEG2, 0 = MPEG2.5
            int layer = (buf[i + 1] >> 1) & 3;     // 1 = Layer III
            int bitrateIndex = buf[i + 2] >> 4;
            int rateIndex = (buf[i + 2] >> 2) & 3;
            if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3) continue;   // 이 프로젝트 mp3는 Layer III만

            bool v1 = version == 3;
            int sampleRate = SampleRateV1[rateIndex] / (v1 ? 1 : version == 2 ? 2 : 4);
            int bitrate = (v1 ? BitrateV1L3 : BitrateV2L3)[bitrateIndex] * 1000;
            int samplesPerFrame = v1 ? 1152 : 576;
            bool mono = (buf[i + 3] >> 6) == 3;

            // VBR 헤더: Xing/Info(사이드 정보 뒤), VBRI(헤더 뒤 32바이트)
            int xing = i + 4 + (v1 ? (mono ? 17 : 32) : (mono ? 9 : 17));
            if (xing + 12 <= n && (Tag(buf, xing, "Xing") || Tag(buf, xing, "Info")))
            {
                uint flags = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(xing + 4));
                if ((flags & 1) != 0)
                    return (double)BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(xing + 8)) * samplesPerFrame / sampleRate;
            }
            int vbri = i + 4 + 32;
            if (vbri + 18 <= n && Tag(buf, vbri, "VBRI"))
                return (double)BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(vbri + 14)) * samplesPerFrame / sampleRate;

            // 고정 비트레이트
            long audioBytes = s.Length - (start + i);
            return audioBytes * 8.0 / bitrate;
        }
        return null;
    }

    private static bool Tag(byte[] b, int at, string tag)
    {
        if (at + tag.Length > b.Length) return false;
        for (int k = 0; k < tag.Length; k++)
            if (b[at + k] != tag[k]) return false;
        return true;
    }
}
