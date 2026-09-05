using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.DebugTools
{
    /// <summary>
    /// 밸런스 튜닝 창. 접속 HUD(<see cref="ConnectionHud"/>, Tab)와 <b>별도의 이동식 창</b>으로,
    /// 기본 토글 키는 <c>F2</c>다. 밸런스 ScriptableObject(이동·귀신·굴착·투척·정신력)의 직렬화
    /// 필드를 런타임 리플렉션으로 읽어 <see cref="HeaderAttribute"/> 그룹별 접이식으로 노출한다 —
    /// 슬라이더(<see cref="RangeAttribute"/>) / 입력칸+−+ / 토글(bool). 값 목록을 손으로 관리하지
    /// 않으므로 SO 에 `[SerializeField]` 필드를 더하면 자동으로 나타난다(`[HideInInspector]` 제외).
    ///
    /// <para><see cref="DrawInline"/> 은 같은 값 줄을 접속 HUD 의 섹션 안에 평면으로 그린다 —
    /// 스킬 수치처럼 상태·제어 버튼과 붙여 놓아야 쓸모 있는 것들을 위한 것이다.</para>
    ///
    /// <para>SO 인스턴스는 세션이 시작되면 씬의 컴포넌트(<c>PlayerMotor._settings</c> 등)에서 찾아
    /// 온다 — 별도 배선이 필요 없다. 수정은 그 플레이 세션 동안 즉시 적용되고, 에디터에서는 SO
    /// 인스턴스가 되돌려지지 않으므로 이후 플레이에도 남는다(디스크 영구 반영은 인스펙터). 빌드에서는
    /// 세션 한정이다. "되돌리기"는 창이 SO 를 처음 붙잡은 시점의 값으로 되돌린다.</para>
    /// </summary>
    internal sealed class TuningHud
    {
        private const int WindowId = 0x7C9137;

        private sealed class Row
        {
            public FieldInfo Field;
            public string Label;
            public string LabelLower;
            public bool IsFloat, IsInt, IsBool, IsEnum;
            public bool HasRange;
            public float RangeMin, RangeMax;
            public bool HasMin;
            public float MinValue;
        }

        private sealed class Group
        {
            public string Name;
            public Row[] Rows;
        }

        private sealed class Target
        {
            public string Title;
            public string ShortTitle;
            public ScriptableObject So;
            public Group[] Groups;
            public Row[] Rows;
            public object[] Snapshot;
            public MethodInfo OnValidate;
            public bool Expanded;
        }

        // (창 제목, 짧은 이름, SO 를 들고 있는 컴포넌트 타입) — 전부 `_settings` 필드에 들어 있다.
        private static readonly (string Title, string Short, Type Owner)[] Sources =
        {
            ("이동 · 시점  (PlayerMoveSettings)", "이동", typeof(PlayerMotor)),
            ("귀신  (GhostPrototypeSettings)", "귀신", typeof(GhostPrototypeController)),
            ("굴착 스킬  (MoleBurrowSettings)", "굴착", typeof(MoleBurrowController)),
            ("탐지 스킬  (DetectionSkillSettings)", "탐지", typeof(DetectionSkillController)),
            ("가구 투척  (FurnitureThrowSettings)", "투척", typeof(FurnitureGrabTarget)),
            ("정신력  (SanitySystemSettings)", "정신력", typeof(SanityNetworkState)),
        };

        private const BindingFlags FieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly List<Target> _targets = new();
        private readonly Dictionary<FieldInfo, string> _buffers = new();
        private readonly HashSet<string> _openGroups = new();
        private float _nextResolveAt;

        private bool _visible;
        private Rect _windowRect = new(430f, 10f, 560f, 660f);
        private Vector2 _scroll;
        private string _filter = string.Empty;

        public bool Visible => _visible;

        public void ToggleVisible() => _visible = !_visible;

        /// <summary>매 OnGUI 에서 호출한다. 창이 꺼져 있으면 아무것도 안 그린다.</summary>
        public void DrawWindow()
        {
            if (!_visible)
                return;

            ResolveTargets();

            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawContents, "튜닝 · 밸런스 값  (F2)");

            // 창이 화면 밖으로 완전히 안 빠지도록 손잡이를 남긴다.
            _windowRect.x = Mathf.Clamp(_windowRect.x, -_windowRect.width + 120f, Screen.width - 120f);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - 40f);
        }

        private void DrawContents(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("필터", GUILayout.Width(30));
            _filter = GUILayout.TextField(_filter ?? string.Empty, GUILayout.MinWidth(120));
            if (GUILayout.Button("지우기", GUILayout.Width(52)))
                _filter = string.Empty;
            if (GUILayout.Button("전체 되돌리기", GUILayout.Width(96)))
                foreach (Target t in _targets)
                    RestoreSnapshot(t);
            if (GUILayout.Button("닫기", GUILayout.Width(44)))
                _visible = false;
            GUILayout.EndHorizontal();

            if (_targets.Count == 0)
            {
                GUILayout.Label("세션을 시작하면(Host) 플레이어·귀신·가구에서 설정을 읽어 옵니다.");
                GUI.DragWindow(new Rect(0, 0, 100000, 22));
                return;
            }

            GUILayout.Label(
                "값은 지금 플레이에 즉시 적용 · 되돌리기 = 이 세션 시작값 · 영구 반영은 인스펙터",
                GUI.skin.label);

            string filter = string.IsNullOrWhiteSpace(_filter) ? null : _filter.Trim().ToLowerInvariant();

            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (Target t in _targets)
            {
                if (filter != null)
                    DrawFiltered(t, filter);
                else
                    DrawFoldout(t);
            }

            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, 100000, 22));
        }

        /// <summary>
        /// 접속 HUD(Tab)의 섹션 안에 설정 하나의 값 줄만 평면으로 그린다. 창(F2)과 <b>같은 SO
        /// 인스턴스·같은 입력 버퍼</b>를 쓰므로 어느 쪽에서 바꾸든 결과가 같고, SO 에
        /// <c>[SerializeField]</c> 를 더하면 양쪽에 동시에 나타난다.
        /// </summary>
        /// <param name="shortTitle"><see cref="Sources"/> 의 짧은 이름(예: "굴착").</param>
        /// <param name="labelWidth">HUD 폭이 창보다 좁아 라벨을 줄인다.</param>
        /// <returns>대상 SO 를 아직 못 찾았으면 false — 세션이 시작되기 전이다.</returns>
        public bool DrawInline(string shortTitle, float labelWidth = 132f)
        {
            ResolveTargets();

            Target target = _targets.Find(x => x.ShortTitle == shortTitle);
            if (target == null)
                return false;

            foreach (Group g in target.Groups)
            {
                GUILayout.Label(Shorten(g.Name), GUI.skin.label);
                foreach (Row row in g.Rows)
                    DrawRow(target, row, labelWidth);
            }

            if (GUILayout.Button("이 수치 되돌리기 (세션 시작값)"))
                RestoreSnapshot(target);

            return true;
        }

        private void DrawFoldout(Target t)
        {
            if (GUILayout.Button((t.Expanded ? "▼  " : "▶  ") + t.Title, SectionStyle))
                t.Expanded = !t.Expanded;

            if (!t.Expanded)
                return;

            if (GUILayout.Button("이 설정 되돌리기", GUILayout.Width(120)))
                RestoreSnapshot(t);

            foreach (Group g in t.Groups)
            {
                string key = t.Title + "\\0" + g.Name;
                bool open = _openGroups.Contains(key);

                string prefix = open ? "  ▼ " : "  ▶ ";
                if (GUILayout.Button(prefix + Shorten(g.Name) + "   (" + g.Rows.Length + ")", GroupStyle))
                {
                    if (open) _openGroups.Remove(key);
                    else _openGroups.Add(key);
                }

                if (!open)
                    continue;

                foreach (Row row in g.Rows)
                    DrawRow(t, row);
            }
        }

        private void DrawFiltered(Target t, string filter)
        {
            bool headerShown = false;
            foreach (Group g in t.Groups)
            {
                bool groupHeaderShown = false;
                foreach (Row row in g.Rows)
                {
                    if (row.LabelLower.IndexOf(filter, StringComparison.Ordinal) < 0)
                        continue;

                    if (!headerShown)
                    {
                        GUILayout.Label("▼  " + t.Title, SectionStyle);
                        headerShown = true;
                    }

                    if (!groupHeaderShown)
                    {
                        GUILayout.Label("  " + Shorten(g.Name), GUI.skin.label);
                        groupHeaderShown = true;
                    }

                    DrawRow(t, row);
                }
            }
        }

        private void ResolveTargets()
        {
            if (_targets.Count == Sources.Length || Time.unscaledTime < _nextResolveAt)
                return;

            _nextResolveAt = Time.unscaledTime + 1f;

            foreach ((string title, string shortTitle, Type owner) in Sources)
            {
                if (_targets.Exists(x => x.Title == title))
                    continue;

                Object component = Object.FindAnyObjectByType(owner);
                if (component == null)
                    continue;

                FieldInfo settingsField = owner.GetField("_settings", FieldFlags);
                var so = settingsField?.GetValue(component) as ScriptableObject;
                if (so == null)
                    continue;

                _targets.Add(BuildTarget(title, shortTitle, so));
            }
        }

        private static Target BuildTarget(string title, string shortTitle, ScriptableObject so)
        {
            Type type = so.GetType();
            var rows = new List<Row>();
            var groups = new List<Group>();
            var current = new List<Row>();
            string currentName = "일반";

            void Flush()
            {
                if (current.Count == 0)
                    return;

                groups.Add(new Group { Name = currentName, Rows = current.ToArray() });
                current = new List<Row>();
            }

            foreach (FieldInfo field in type.GetFields(FieldFlags))
            {
                if (field.IsLiteral || field.IsInitOnly)
                    continue;
                if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
                    continue;
                if (field.GetCustomAttribute<HideInInspector>() != null)
                    continue;

                Type ft = field.FieldType;
                var row = new Row { Field = field, Label = Nicify(field.Name) };
                row.LabelLower = row.Label.ToLowerInvariant();

                if (ft == typeof(float)) row.IsFloat = true;
                else if (ft == typeof(int)) row.IsInt = true;
                else if (ft == typeof(bool)) row.IsBool = true;
                else if (ft.IsEnum) row.IsEnum = true;
                else continue;

                string header = field.GetCustomAttribute<HeaderAttribute>()?.header;
                if (header != null)
                {
                    Flush();
                    currentName = header;
                }

                RangeAttribute range = field.GetCustomAttribute<RangeAttribute>();
                if (range != null)
                {
                    row.HasRange = true;
                    row.RangeMin = range.min;
                    row.RangeMax = range.max;
                }

                MinAttribute min = field.GetCustomAttribute<MinAttribute>();
                if (min != null)
                {
                    row.HasMin = true;
                    row.MinValue = min.min;
                }

                rows.Add(row);
                current.Add(row);
            }

            Flush();

            var target = new Target
            {
                Title = title,
                ShortTitle = shortTitle,
                So = so,
                Groups = groups.ToArray(),
                Rows = rows.ToArray(),
                Snapshot = new object[rows.Count],
                OnValidate = type.GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic),
            };

            for (int i = 0; i < rows.Count; i++)
                target.Snapshot[i] = rows[i].Field.GetValue(so);

            return target;
        }

        private void DrawRow(Target t, Row row, float labelWidth = 190f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(row.Label, GUILayout.Width(labelWidth));

            object current = row.Field.GetValue(t.So);

            if (row.IsBool)
            {
                bool b = (bool)current;
                bool nb = GUILayout.Toggle(b, b ? " on" : " off");
                if (nb != b)
                    Assign(t, row, nb);
            }
            else if (row.IsEnum)
            {
                if (GUILayout.Button(current.ToString()))
                    Assign(t, row, NextEnum(row.Field.FieldType, current));
            }
            else
            {
                float value = row.IsFloat ? (float)current : (int)current;

                if (row.HasRange)
                {
                    GUI.changed = false;
                    float slid = GUILayout.HorizontalSlider(
                        value, row.RangeMin, row.RangeMax, GUILayout.MinWidth(90));
                    if (GUI.changed && !Mathf.Approximately(slid, value))
                    {
                        value = slid;
                        Assign(t, row, Box(row, value));
                        _buffers[row.Field] = Fmt(row, value);
                    }
                }
                else
                {
                    if (GUILayout.Button("−", GUILayout.Width(24)))
                    {
                        value = Step(row, value, -1);
                        Assign(t, row, Box(row, value));
                        _buffers[row.Field] = Fmt(row, value);
                    }

                    if (GUILayout.Button("+", GUILayout.Width(24)))
                    {
                        value = Step(row, value, +1);
                        Assign(t, row, Box(row, value));
                        _buffers[row.Field] = Fmt(row, value);
                    }
                }

                if (!_buffers.TryGetValue(row.Field, out string buffer))
                    buffer = Fmt(row, value);

                string typed = GUILayout.TextField(buffer, GUILayout.Width(64));
                if (typed != buffer)
                {
                    _buffers[row.Field] = typed;
                    if (float.TryParse(
                            typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    {
                        Assign(t, row, Box(row, parsed));
                    }
                }
            }

            GUILayout.EndHorizontal();
        }

        private void RestoreSnapshot(Target t)
        {
            for (int i = 0; i < t.Rows.Length; i++)
            {
                t.Rows[i].Field.SetValue(t.So, t.Snapshot[i]);
                _buffers.Remove(t.Rows[i].Field);
            }

            InvokeOnValidate(t);
        }

        private static void Assign(Target t, Row row, object value)
        {
            row.Field.SetValue(t.So, value);
            InvokeOnValidate(t);
        }

        private static void InvokeOnValidate(Target t)
        {
            // SO 가 상호 의존 필드를 다시 클램프하게 한다(예: crouchMoveSpeed ≤ moveSpeed).
            try { t.OnValidate?.Invoke(t.So, null); }
            catch { /* 튜닝 창은 검증 실패로 멈추지 않는다 */ }
        }

        private static object Box(Row row, float v)
        {
            if (row.HasRange)
                v = Mathf.Clamp(v, row.RangeMin, row.RangeMax);
            if (row.HasMin)
                v = Mathf.Max(v, row.MinValue);

            return row.IsFloat ? v : Mathf.RoundToInt(v);
        }

        private static float Step(Row row, float v, int dir)
        {
            float step = row.HasRange
                ? (row.RangeMax - row.RangeMin) / 50f
                : Mathf.Max(row.IsInt ? 1f : 0.01f, Mathf.Abs(v) * 0.1f);

            if (row.IsInt)
                step = Mathf.Max(1f, Mathf.Round(step));

            return v + step * dir;
        }

        private static string Fmt(Row row, float v)
        {
            return row.IsInt
                ? Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static object NextEnum(Type enumType, object current)
        {
            Array values = Enum.GetValues(enumType);
            int index = Array.IndexOf(values, current);
            return values.GetValue((index + 1) % values.Length);
        }

        /// <summary>긴 <c>[Header]</c> 문자열(§ 참조·미결정 메모까지 붙은 것)을 버튼 한 줄에 맞게 줄인다.</summary>
        private static string Shorten(string s)
        {
            const int max = 46;
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s;

            // 첫 구분자(공백·중점·괄호·대시) 전까지만, 그래도 길면 잘라낸다.
            int cut = s.IndexOfAny(new[] { '(', '·', '—', '-' });
            if (cut > 4 && cut <= max)
                return s.Substring(0, cut).TrimEnd() + " …";

            return s.Substring(0, max - 1).TrimEnd() + "…";
        }

        private static string Nicify(string name)
        {
            if (name.Length > 0 && name[0] == '_')
                name = name.Substring(1);

            var sb = new StringBuilder(name.Length + 6);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                    sb.Append(' ');

                sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }

            return sb.ToString();
        }

        private static GUIStyle _sectionStyle;
        private static GUIStyle _groupStyle;

        private static GUIStyle SectionStyle => _sectionStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            margin = new RectOffset(0, 0, 5, 1),
        };

        private static GUIStyle GroupStyle => _groupStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            margin = new RectOffset(10, 0, 1, 1),
        };
    }
}
