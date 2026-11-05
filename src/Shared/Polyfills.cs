// .NET Framework 4.8에는 없는 컴파일러 지원 형식을 선언한다(내부 형식, 실행 코드는 없다).
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute { public string FeatureName { get; } = featureName; }
}
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter)] internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute { public bool ReturnValue { get; } = returnValue; }
    [AttributeUsage(AttributeTargets.Parameter)] internal sealed class MaybeNullWhenAttribute(bool returnValue) : Attribute { public bool ReturnValue { get; } = returnValue; }
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue)] internal sealed class NotNullAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue | AttributeTargets.Field)] internal sealed class MaybeNullAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue | AttributeTargets.Field)] internal sealed class AllowNullAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true)]
    internal sealed class NotNullIfNotNullAttribute(string parameterName) : Attribute { public string ParameterName { get; } = parameterName; }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, AllowMultiple = true)]
    internal sealed class MemberNotNullAttribute : Attribute { public MemberNotNullAttribute(string member) { Members = new[] { member }; } public MemberNotNullAttribute(params string[] members) { Members = members; } public string[] Members { get; } }
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}
namespace System
{
    // 범위 연산자(s[a..b], s[^1])를 쓰기 위한 최소 선언
    internal readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;
        public Index(int value, bool fromEnd = false) { _value = fromEnd ? ~value : value; }
        public static Index Start => new Index(0);
        public static Index End => new Index(~0);
        public static implicit operator Index(int value) => new Index(value);
        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;
        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;
        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object? obj) => obj is Index i && i._value == _value;
        public override int GetHashCode() => _value;
    }
    internal readonly struct Range : IEquatable<Range>
    {
        public Range(Index start, Index end) { Start = start; End = end; }
        public Index Start { get; }
        public Index End { get; }
        public static Range All => new Range(Index.Start, Index.End);
        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            var s = Start.GetOffset(length);
            var e = End.GetOffset(length);
            if ((uint)e > (uint)length || (uint)s > (uint)e) throw new ArgumentOutOfRangeException(nameof(length));
            return (s, e - s);
        }
        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? obj) => obj is Range r && Equals(r);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
    }
}
namespace System.Collections.Generic
{
    internal static class KeyValuePairExtensions
    {
        public static TValue? GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> d, TKey key)
            => d.TryGetValue(key, out var v) ? v : default;
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value) { key = pair.Key; value = pair.Value; }
    }
}
namespace ComPortWidget
{
    /// <summary>Math.Clamp, double.IsFinite 대용(.NET Framework 4.8에 없다).</summary>
    internal static class Num
    {
        public static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }
}
