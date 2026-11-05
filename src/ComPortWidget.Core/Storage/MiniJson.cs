using System.Globalization;
using System.Text;

namespace ComPortWidget.Core.Storage;

/// <summary>JSON 문법 오류. 호출부는 손상된 파일로 취급한다.</summary>
public sealed class JsonFormatException : Exception
{
    public JsonFormatException(string message) : base(message) { }
}

/// <summary>숫자 토큰. 원문을 그대로 들고 있다가 필요한 형식으로 바꾼다.</summary>
public readonly struct JsonNumber
{
    public JsonNumber(string raw) { Raw = raw; }
    public string Raw { get; }
    public bool TryGetInt32(out int value)
        => int.TryParse(Raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    public bool TryGetDouble(out double value)
        => double.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsInfinity(value);
}

/// <summary>
/// .NET Framework 4.8에 들어 있는 직렬화기는 날짜, 열거형, 한글 출력이 이전 파일과 같은 값으로 읽고 쓰기 어려워서
/// 이전 형식으로 쓴 파일을 같은 값으로 읽고 같은 모양으로 쓰는 작은 JSON 구현을 둔다(바이트 단위 동일성은 목표가 아니다). 외부 라이브러리를 쓰지 않는다.
/// 값은 Dictionary&lt;string, object?&gt;, List&lt;object?&gt;, string, bool, JsonNumber, null로 표현한다.
/// </summary>
public static class MiniJson
{
    private const int MaxDepth = 64;
    private const string IsoOffsetFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz";
    private const string IsoUtcFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public static object? Parse(string text)
    {
        var p = new Parser(text);
        p.SkipWs();
        var v = p.ReadValue(0);
        p.SkipWs();
        if (!p.AtEnd) throw new JsonFormatException("Unexpected trailing characters.");
        return v;
    }

    /// <summary>UTF-8(BOM 허용) 바이트를 읽는다. 잘못된 UTF-8은 오류로 본다.</summary>
    public static object? Parse(byte[] utf8)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(utf8); }
        catch (ArgumentException e) { throw new JsonFormatException(e.Message); }
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        return Parse(text);
    }

    /// <summary>2칸 들여쓰기로 쓴다. 한글과 기호는 이스케이프하지 않고, 제어 문자와 따옴표, 역슬래시만 이스케이프한다.</summary>
    public static byte[] ToUtf8(object? value)
    {
        var sb = new StringBuilder();
        Write(sb, value, 0);
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private static void Write(StringBuilder sb, object? v, int indent)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case string s: WriteString(sb, s); break;
            case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
            case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
            case double d:
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new JsonFormatException("Number is not finite.");
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case JsonNumber n: sb.Append(n.Raw); break;
            case DateTimeOffset t: WriteString(sb, t.ToString("o", CultureInfo.InvariantCulture)); break;
            case System.Collections.IDictionary dict:
                if (dict.Count == 0) { sb.Append("{}"); break; }
                sb.Append("{\n");
                var first = true;
                foreach (System.Collections.DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append(' ', indent + 2);
                    WriteString(sb, (string)e.Key);
                    sb.Append(": ");
                    Write(sb, e.Value, indent + 2);
                }
                sb.Append('\n').Append(' ', indent).Append('}');
                break;
            case System.Collections.IEnumerable list:
                var items = list.Cast<object?>().ToList();
                if (items.Count == 0) { sb.Append("[]"); break; }
                sb.Append("[\n");
                for (var k = 0; k < items.Count; k++)
                {
                    if (k > 0) sb.Append(",\n");
                    sb.Append(' ', indent + 2);
                    Write(sb, items[k], indent + 2);
                }
                sb.Append('\n').Append(' ', indent).Append(']');
                break;
            default: throw new JsonFormatException("Unsupported value type: " + v.GetType().Name);
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // ---- 읽기 도우미: 형식이 다르면 JsonFormatException ----

    public static Dictionary<string, object?> AsObject(object? v)
        => v as Dictionary<string, object?> ?? throw new JsonFormatException("Object expected.");

    public static List<object?> AsArray(object? v)
        => v as List<object?> ?? throw new JsonFormatException("Array expected.");

    /// <summary>null이거나 없으면 null. 문자열이 아니면 오류.</summary>
    public static string? OptString(Dictionary<string, object?> o, string key)
    {
        if (!o.TryGetValue(key, out var v) || v is null) return null;
        return v as string ?? throw new JsonFormatException(key + " is not a string.");
    }

    public static bool OptBool(Dictionary<string, object?> o, string key, bool fallback)
    {
        if (!o.TryGetValue(key, out var v) || v is null) return fallback;
        return v is bool b ? b : throw new JsonFormatException(key + " is not a bool.");
    }

    public static int OptInt(Dictionary<string, object?> o, string key, int fallback)
    {
        if (!o.TryGetValue(key, out var v) || v is null) return fallback;
        return v is JsonNumber n && n.TryGetInt32(out var i) ? i : throw new JsonFormatException(key + " is not an integer.");
    }

    public static double? OptDouble(Dictionary<string, object?> o, string key)
    {
        if (!o.TryGetValue(key, out var v) || v is null) return null;
        return v is JsonNumber n && n.TryGetDouble(out var d) ? d : throw new JsonFormatException(key + " is not a number.");
    }

    public static DateTimeOffset? OptDate(Dictionary<string, object?> o, string key)
    {
        if (!o.TryGetValue(key, out var v) || v is null) return null;
        // 이전 버전이 쓴 ISO 8601 형식만 받는다(소수 초는 0~7자리, 끝은 Z 또는 +hh:mm)
        if (v is string s)
        {
            if (DateTimeOffset.TryParseExact(s, IsoOffsetFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return t;
            if (DateTimeOffset.TryParseExact(s, IsoUtcFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out t)) return t;
        }
        throw new JsonFormatException(key + " is not a date.");
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; }

        public bool AtEnd => _i >= _s.Length;

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        public void SkipWs()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
        }

        public object? ReadValue(int depth)
        {
            if (depth > MaxDepth) throw new JsonFormatException("Nesting too deep.");
            if (AtEnd) throw new JsonFormatException("Unexpected end.");
            switch (_s[_i])
            {
                case '{': return ReadObject(depth);
                case '[': return ReadArray(depth);
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default: return ReadNumber();
            }
        }

        private void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new JsonFormatException("Invalid literal.");
            _i += word.Length;
        }

        private Dictionary<string, object?> ReadObject(int depth)
        {
            var o = new Dictionary<string, object?>(StringComparer.Ordinal);
            _i++;
            SkipWs();
            if (!AtEnd && _s[_i] == '}') { _i++; return o; }
            while (true)
            {
                SkipWs();
                if (AtEnd || _s[_i] != '"') throw new JsonFormatException("Property name expected.");
                var key = ReadString();
                SkipWs();
                if (AtEnd || _s[_i] != ':') throw new JsonFormatException("':' expected.");
                _i++;
                SkipWs();
                o[key] = ReadValue(depth + 1); // 같은 이름이 또 나오면 뒤의 값을 쓴다
                SkipWs();
                if (AtEnd) throw new JsonFormatException("Unexpected end.");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; return o; }
                throw new JsonFormatException("',' or '}' expected.");
            }
        }

        private List<object?> ReadArray(int depth)
        {
            var a = new List<object?>();
            _i++;
            SkipWs();
            if (!AtEnd && _s[_i] == ']') { _i++; return a; }
            while (true)
            {
                SkipWs();
                a.Add(ReadValue(depth + 1));
                SkipWs();
                if (AtEnd) throw new JsonFormatException("Unexpected end.");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; return a; }
                throw new JsonFormatException("',' or ']' expected.");
            }
        }

        private string ReadString()
        {
            _i++; // 여는 따옴표
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw new JsonFormatException("Unterminated string.");
                var c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw new JsonFormatException("Control character in string.");
                if (c != '\\') { sb.Append(c); continue; }
                if (AtEnd) throw new JsonFormatException("Unterminated escape.");
                var e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) throw new JsonFormatException("Invalid \\u escape.");
                        var hex = _s.Substring(_i, 4);
                        if (!hex.All(Uri.IsHexDigit)) throw new JsonFormatException("Invalid \\u escape.");
                        sb.Append((char)int.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                        _i += 4;
                        break;
                    default: throw new JsonFormatException("Invalid escape.");
                }
            }
        }

        private JsonNumber ReadNumber()
        {
            var start = _i;
            if (_i < _s.Length && _s[_i] == '-') _i++;
            var digits = 0;
            while (_i < _s.Length && IsDigit(_s[_i])) { _i++; digits++; }
            if (digits == 0) throw new JsonFormatException("Invalid value.");
            // JSON은 0 앞에 숫자를 더 붙이는 표기(01, -007)를 허용하지 않는다
            var firstDigit = start + (_s[start] == '-' ? 1 : 0);
            if (digits > 1 && _s[firstDigit] == '0') throw new JsonFormatException("Leading zero.");
            if (_i < _s.Length && _s[_i] == '.')
            {
                _i++;
                var frac = 0;
                while (_i < _s.Length && IsDigit(_s[_i])) { _i++; frac++; }
                if (frac == 0) throw new JsonFormatException("Invalid number.");
            }
            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                var exp = 0;
                while (_i < _s.Length && IsDigit(_s[_i])) { _i++; exp++; }
                if (exp == 0) throw new JsonFormatException("Invalid number.");
            }
            return new JsonNumber(_s.Substring(start, _i - start));
        }
    }
}
