using System.Text;
using System.Text.RegularExpressions;
using SCResourceGrabber.Models;

namespace SCResourceGrabber.Services;

/// <summary>
/// URL 확장자 → 파일 내용(매직 바이트/텍스트 패턴) → Content-Type 순으로 리소스 종류를 판정한다.
/// URL에 확장자가 없거나 해시 이름인 경우가 많아 내용 판정을 우선 신뢰한다.
/// </summary>
public static class ResourceClassifier
{
    public record Result(ResourceCategory Category, string Extension, string SubType = "");

    private static readonly Dictionary<string, ResourceCategory> ExtMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = ResourceCategory.Image, ["jpg"] = ResourceCategory.Image, ["jpeg"] = ResourceCategory.Image,
        ["gif"] = ResourceCategory.Image, ["webp"] = ResourceCategory.Image, ["avif"] = ResourceCategory.Image,
        ["bmp"] = ResourceCategory.Image, ["svg"] = ResourceCategory.Image, ["ico"] = ResourceCategory.Image,
        ["mp3"] = ResourceCategory.Audio, ["m4a"] = ResourceCategory.Audio, ["aac"] = ResourceCategory.Audio,
        ["ogg"] = ResourceCategory.Audio, ["wav"] = ResourceCategory.Audio, ["flac"] = ResourceCategory.Audio,
        ["opus"] = ResourceCategory.Audio,
        ["mp4"] = ResourceCategory.Video, ["webm"] = ResourceCategory.Video, ["m4v"] = ResourceCategory.Video,
        ["woff"] = ResourceCategory.Font, ["woff2"] = ResourceCategory.Font, ["ttf"] = ResourceCategory.Font,
        ["otf"] = ResourceCategory.Font,
        ["atlas"] = ResourceCategory.Spine, ["skel"] = ResourceCategory.Spine,
        ["json"] = ResourceCategory.Json,
    };

    private static readonly Regex AtlasHeader = new(
        @"^\s*\S+\.(png|webp|jpg|jpeg)\s*\r?\n(\s*\w+\s*:.*\r?\n){0,6}?\s*(size|format|filter)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Result Classify(string url, string mime, byte[] data)
    {
        string urlExt = GetUrlExtension(url);
        mime = mime.ToLowerInvariant();

        // 1) 내용 기반 판정
        var sniffed = Sniff(data);
        if (sniffed != null)
        {
            // 텍스트 계열(JSON)이고 URL이 .atlas/.skel처럼 더 구체적이면 URL 쪽 우선
            if (sniffed.Category == ResourceCategory.Json && urlExt.Length > 0 && ExtMap.TryGetValue(urlExt, out var c) && c == ResourceCategory.Spine)
                return new Result(ResourceCategory.Spine, urlExt, urlExt);
            return sniffed with { Extension = urlExt.Length > 0 && ExtMap.ContainsKey(urlExt) ? urlExt : sniffed.Extension };
        }

        // 2) URL 확장자
        if (urlExt.Length > 0 && ExtMap.TryGetValue(urlExt, out var cat))
            return new Result(cat, urlExt, cat == ResourceCategory.Spine ? urlExt : "");

        // 3) Content-Type
        if (mime.StartsWith("image/")) return new Result(ResourceCategory.Image, MimeExt(mime) ?? "img");
        if (mime.StartsWith("audio/")) return new Result(ResourceCategory.Audio, MimeExt(mime) ?? "audio");
        if (mime.StartsWith("video/")) return new Result(ResourceCategory.Video, MimeExt(mime) ?? "video");
        if (mime.Contains("font")) return new Result(ResourceCategory.Font, "font");
        if (mime.Contains("json")) return new Result(ResourceCategory.Json, "json");

        return new Result(ResourceCategory.Other, urlExt.Length > 0 ? urlExt : MimeExt(mime) ?? "bin");
    }

    private static Result? Sniff(byte[] d)
    {
        if (d.Length < 4) return null;

        bool At(int offset, string ascii) =>
            d.Length >= offset + ascii.Length && Encoding.ASCII.GetString(d, offset, ascii.Length) == ascii;

        if (d[0] == 0x89 && At(1, "PNG")) return new(ResourceCategory.Image, "png");
        if (d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return new(ResourceCategory.Image, "jpg");
        if (At(0, "GIF8")) return new(ResourceCategory.Image, "gif");
        if (At(0, "RIFF") && At(8, "WEBP")) return new(ResourceCategory.Image, "webp");
        if (At(0, "RIFF") && At(8, "WAVE")) return new(ResourceCategory.Audio, "wav");
        if (At(0, "OggS")) return new(ResourceCategory.Audio, "ogg");
        if (At(0, "fLaC")) return new(ResourceCategory.Audio, "flac");
        if (At(0, "ID3") || (d[0] == 0xFF && (d[1] & 0xE0) == 0xE0 && (d[1] & 0x06) != 0)) return new(ResourceCategory.Audio, "mp3");
        if (At(4, "ftyp"))
        {
            string brand = d.Length >= 12 ? Encoding.ASCII.GetString(d, 8, 4) : "";
            if (brand.StartsWith("M4A") || brand.StartsWith("M4B")) return new(ResourceCategory.Audio, "m4a");
            if (brand.StartsWith("avif") || brand.StartsWith("avis")) return new(ResourceCategory.Image, "avif");
            return new(ResourceCategory.Video, "mp4");
        }
        if (d[0] == 0x1A && d[1] == 0x45 && d[2] == 0xDF && d[3] == 0xA3) return new(ResourceCategory.Video, "webm");
        if (At(0, "wOFF")) return new(ResourceCategory.Font, "woff");
        if (At(0, "wOF2")) return new(ResourceCategory.Font, "woff2");

        // 텍스트 계열: 앞부분만 보고 판정
        string head = Encoding.UTF8.GetString(d, 0, Math.Min(d.Length, 64 * 1024)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (head.StartsWith('{') || head.StartsWith('['))
        {
            if (head.Contains("\"skeleton\"") && head.Contains("\"bones\""))
                return new(ResourceCategory.Spine, "json", "skeleton json");
            return new(ResourceCategory.Json, "json");
        }
        if (AtlasHeader.IsMatch(head)) return new(ResourceCategory.Spine, "atlas", "atlas");
        if (head.StartsWith("<svg") || (head.StartsWith("<?xml") && head.Contains("<svg"))) return new(ResourceCategory.Image, "svg");

        return null;
    }

    public static string GetUrlExtension(string url)
    {
        try
        {
            var last = new Uri(url).AbsolutePath.Split('/').LastOrDefault() ?? "";
            var m = Regex.Match(last, @"\.([A-Za-z0-9]{1,6})$");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
        }
        catch { return ""; }
    }

    private static string? MimeExt(string mime) => mime.Split(';')[0].Trim() switch
    {
        "image/png" => "png", "image/jpeg" => "jpg", "image/gif" => "gif", "image/webp" => "webp",
        "image/avif" => "avif", "image/svg+xml" => "svg",
        "audio/mpeg" => "mp3", "audio/mp4" => "m4a", "audio/aac" => "aac", "audio/ogg" => "ogg",
        "audio/wav" or "audio/x-wav" => "wav",
        "video/mp4" => "mp4", "video/webm" => "webm",
        "application/json" => "json", "text/plain" => "txt", "text/html" => "html",
        "text/css" => "css", "application/javascript" or "text/javascript" => "js",
        _ => null
    };
}
