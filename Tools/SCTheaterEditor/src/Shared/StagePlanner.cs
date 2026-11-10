#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace UsoSCTheater.Shared
{
    /// <summary>캐릭터 1명의 지금 자리 (Index가 Count를 넘으면 빈 칸이 없어 화면 밖)</summary>
    public sealed class StagePlacement
    {
        public string Key { get; }
        public int Count { get; }
        public int Index { get; }
        public bool IsExplicit { get; }
        public int Order { get; }      //클수록 앞 (최근에 CG를 지정)
        public double X { get; }
        public double Scale { get; }
        public double BaseY { get; }
        public string Pos => StageLayout.FormatPos(Count, Index);

        internal StagePlacement(string key, int count, int index, bool isExplicit, int order, double x, double scale, double baseY)
        {
            Key = key;
            Count = count;
            Index = index;
            IsExplicit = isExplicit;
            Order = order;
            X = x;
            Scale = scale;
            BaseY = baseY;
        }
    }

    /// <summary>
    /// 무대 자리 규칙 (2026-11-10 결정, 회의록 '무대 배치 재정립') — 게임·TE 무대 동기화·카탈로그 동기화 검사가 같은 규칙으로 돌림
    /// 1) Pos "N-i" = 적힌 그대로. Pos 오류(형식·표에 없는 N·1~N 밖)는 경고 후 이 줄에서 건너뜀
    /// 2) 무대에 명시 캐릭터가 있는 동안 다른 N 명시는 건너뜀 (비교는 자기 자신 제외 — 혼자면 분할 변경 허용)
    /// 3) Pos가 빈 캐릭터(자동): 기준 N(명시가 있으면 그 N, 없으면 무대 인원, 표에 없으면 가장 큰 행)의 빈 칸을 등장 순서대로 왼쪽부터.
    ///    칸이 모자라면 N을 넘는 칸 번호 그대로(화면 밖) + 경고
    /// 4) 이미 무대에 있는 캐릭터의 빈 Pos = 자리 유지. 다시 배치는 새 캐릭터가 등장하거나 명시 자리가 바뀔 때만 (퇴장 때는 안 함)
    /// 5) 앞뒤: StartLine 한 번에 1씩 오르는 순서 — 같은 줄(CGGROUP 멤버)은 같은 값
    /// Show 뒤 무대 전원의 자리가 바뀔 수 있음 → 부르는 쪽은 Placements()를 다시 적용 (등장과 함께 바로 이동)
    /// 키는 대소문자 무시. 경고 문구에는 줄 번호가 없음 — 부르는 쪽이 붙임
    /// </summary>
    public sealed class StagePlanner
    {
        private sealed class Actor
        {
            public Actor(string key, int entry)
            {
                Key = key;
                Entry = entry;
            }
            public string Key { get; }
            public int Entry { get; }   //등장 순서
            public bool Explicit;
            public int Count;
            public int Index;
            public int Order;
        }

        private readonly StageTable table;
        private readonly Dictionary<string, Actor> actors = new Dictionary<string, Actor>(StringComparer.OrdinalIgnoreCase);
        private int entrySeq;
        private int orderSeq;

        public StagePlanner(StageTable table) { this.table = table; }

        public StageTable Table => table;
        public bool Contains(string key) => actors.ContainsKey(key);

        //TEXT·SETCG 1줄마다 Show 전에 1번 (앞뒤 순서)
        public void StartLine() => orderSeq++;

        //TRANSITION·CG="none" — 무대를 비움
        public void Clear() => actors.Clear();

        //SETCG clean — 내리기만, 남은 캐릭터는 옮기지 않음
        public void Remove(string key) => actors.Remove(key);

        //무대에 올렸으면(이미 있던 캐릭터 포함) true, 건너뛰었으면 false + 경고
        public bool Show(string key, string? pos, ICollection<string> warnings)
        {
            if (table.Rows.Count == 0)
            {
                warnings.Add($"{key}: StageData에 행이 없음 → 이 줄에서 건너뜀");
                return false;
            }
            actors.TryGetValue(key, out Actor? actor);

            if (string.IsNullOrWhiteSpace(pos))
            {
                if (actor != null)
                {
                    actor.Order = orderSeq;   //자리 유지, 앞으로만
                    return true;
                }
                actors[key] = new Actor(key, ++entrySeq) { Order = orderSeq };
                Relayout(warnings);
                return true;
            }

            string p = pos!.Trim();
            if (!StageLayout.TryParsePos(p, out int count, out int index) || !table.TryGetRow(count, out _) || index < 1 || index > count)
            {
                warnings.Add($"{key}: Pos=\"{p}\" 오류 (N-i — N은 StageData에 있는 분할, i는 1~N) → 이 줄에서 건너뜀");
                return false;
            }
            foreach (Actor other in actors.Values)
            {
                if (other == actor || !other.Explicit || other.Count == count) continue;
                warnings.Add($"{key}: 무대에 {other.Key}가 {other.Count}분할로 명시되어 있어 Pos=\"{p}\"는 이 줄에서 건너뜀");
                return false;
            }

            bool changed = actor == null || !actor.Explicit || actor.Count != count || actor.Index != index;
            if (actor == null) actors[key] = actor = new Actor(key, ++entrySeq);
            actor.Explicit = true;
            actor.Count = count;
            actor.Index = index;
            actor.Order = orderSeq;
            if (changed) Relayout(warnings);
            return true;
        }

        public bool TryGetPlacement(string key, [NotNullWhen(true)] out StagePlacement? placement)
        {
            placement = null;
            if (!actors.TryGetValue(key, out Actor? a)) return false;
            placement = ToPlacement(a);
            return true;
        }

        //무대 캐릭터 전원, 뒤 → 앞 순서 (앞뒤 정렬에 그대로 사용)
        public IReadOnlyList<StagePlacement> Placements() =>
            actors.Values.OrderBy(a => a.Order).ThenBy(a => a.Entry).Select(ToPlacement).ToList();

        private void Relayout(ICollection<string> warnings)
        {
            Actor? anyExplicit = actors.Values.FirstOrDefault(a => a.Explicit);   //명시끼리는 N이 같음 (규칙 2)
            int n = anyExplicit?.Count ?? actors.Count;
            if (!table.TryGetRow(n, out _))
            {
                int largest = table.Rows[table.Rows.Count - 1].Count;
                warnings.Add($"자동 배치: {n}명 행이 StageData에 없음 → {largest}분할 사용");
                n = largest;
            }

            var taken = new HashSet<int>(actors.Values.Where(a => a.Explicit).Select(a => a.Index));
            int next = 1;
            foreach (Actor a in actors.Values.Where(a => !a.Explicit).OrderBy(a => a.Entry))
            {
                while (taken.Contains(next)) next++;
                if (next > n) warnings.Add($"{a.Key}: {n}분할에 빈 칸이 없음 → {next}번째 칸(화면 밖)");
                a.Count = n;
                a.Index = next++;
            }
        }

        private StagePlacement ToPlacement(Actor a)
        {
            table.TryGetRow(a.Count, out StageRow? row);   //Show·Relayout이 표에 있는 N만 남김
            return new StagePlacement(a.Key, a.Count, a.Index, a.Explicit, a.Order, row!.SlotX(a.Index), row.Scale, row.BaseY);
        }
    }
}
