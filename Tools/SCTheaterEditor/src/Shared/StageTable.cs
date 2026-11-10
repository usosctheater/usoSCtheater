#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace UsoSCTheater.Shared
{
    /// <summary>인원별 배치 1행 (StageData.xml의 Row). 좌표는 1920×1080 화면 중심 기준 px, 위가 +</summary>
    public sealed class StageRow
    {
        public int Count { get; }      //분할 수 N
        public double Width { get; }   //사용 폭
        public double Scale { get; }   //크기 (게임 단위, 100 = Spine 원래 크기)
        public double BaseY { get; }   //바닥선 Y

        public StageRow(int count, double width, double scale, double baseY)
        {
            Count = count;
            Width = width;
            Scale = scale;
            BaseY = baseY;
        }

        //i번째 칸의 X (i가 N을 넘으면 화면 밖)
        public double SlotX(int index) => StageLayout.SlotX(Width, Count, index);
    }

    /// <summary>대사창 안내선 (TE 무대 표시용 — 화면 중심 기준 가운데 X·Y + 크기). 게임은 쓰지 않음</summary>
    public sealed class StageGuide
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public StageGuide(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// 무대 배치 표 (StageData.xml 1개). 파일 읽기는 게임(Resources)·TE(Data\ 복사본)가 각자, 해석은 여기서만
    /// 형식: &lt;StageData&gt; &lt;Row Count="3" Width="1440" Scale="100" BaseY="-180" /&gt; … &lt;DialogGuide X="0" Y="-381" Width="1344" Height="259" /&gt;
    /// 숫자 값의 원본은 이 파일 하나 — 코드 기본값 없음 (파일이 없거나 깨지면 빈 표 + 경고 → 캐릭터가 나오지 않음). 속성 이름은 대소문자 구분
    /// </summary>
    public sealed class StageTable
    {
        public const string DataName = "StageData";   //Resources/Data/StageData.xml, TE Data\StageData.xml

        private readonly Dictionary<int, StageRow> byCount = new Dictionary<int, StageRow>();

        public IReadOnlyList<StageRow> Rows { get; }   //Count 오름차순
        public StageGuide? DialogGuide { get; }        //없으면 TE가 안내선을 그리지 않음

        public StageTable(IEnumerable<StageRow> rows, StageGuide? dialogGuide)
        {
            foreach (StageRow r in rows) byCount[r.Count] = r;
            Rows = byCount.Values.OrderBy(r => r.Count).ToList();
            DialogGuide = dialogGuide;
        }

        public bool TryGetRow(int count, [NotNullWhen(true)] out StageRow? row) => byCount.TryGetValue(count, out row);

        //XML 문자열 → 표. 문제마다 warnings에 한 줄씩 남기고 그 부분만 뺌 (예외를 던지지 않음)
        public static StageTable Parse(string? xml, ICollection<string> warnings)
        {
            var empty = new StageTable(Array.Empty<StageRow>(), null);
            if (string.IsNullOrWhiteSpace(xml))
            {
                warnings.Add("StageData가 없거나 비어 있음 → 무대에 캐릭터가 나오지 않음");
                return empty;
            }

            XElement? root;
            try { root = XDocument.Parse(xml!).Root; }
            catch (XmlException e)
            {
                warnings.Add($"StageData XML 오류 → 무대에 캐릭터가 나오지 않음 ({e.Message})");
                return empty;
            }
            if (root == null)
            {
                warnings.Add("StageData 루트가 없음 → 무대에 캐릭터가 나오지 않음");
                return empty;
            }

            var rows = new List<StageRow>();
            var seen = new HashSet<int>();
            foreach (XElement e in root.Elements("Row"))
            {
                if (!TryInt(e, "Count", out int count) || count < 1
                    || !TryNumber(e, "Width", out double width) || !TryNumber(e, "Scale", out double scale) || !TryNumber(e, "BaseY", out double baseY))
                {
                    warnings.Add($"StageData Row 값 오류 → 이 행 건너뜀: {e}");
                    continue;
                }
                if (!seen.Add(count))
                {
                    warnings.Add($"StageData Row Count=\"{count}\" 중복 → 첫 행 사용");
                    continue;
                }
                rows.Add(new StageRow(count, width, scale, baseY));
            }
            if (rows.Count == 0) warnings.Add("StageData에 쓸 수 있는 Row가 없음 → 무대에 캐릭터가 나오지 않음");

            StageGuide? guide = null;
            XElement? g = root.Element("DialogGuide");
            if (g == null) warnings.Add("StageData에 DialogGuide가 없음 → TE 대사창 안내선 없음");
            else if (TryNumber(g, "X", out double gx) && TryNumber(g, "Y", out double gy) && TryNumber(g, "Width", out double gw) && TryNumber(g, "Height", out double gh))
                guide = new StageGuide(gx, gy, gw, gh);
            else warnings.Add($"StageData DialogGuide 값 오류 → TE 대사창 안내선 없음: {g}");

            return new StageTable(rows, guide);
        }

        private static bool TryNumber(XElement e, string name, out double value)
        {
            value = 0;
            string? text = (string?)e.Attribute(name);
            return text != null && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryInt(XElement e, string name, out int value)
        {
            value = 0;
            string? text = (string?)e.Attribute(name);
            return text != null && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
