using System;

namespace HelixEditor
{
    /// <summary>An asset's guid: 128 bits, as text 32 hex digits (the .meta's guid line).</summary>
    [Serializable]
    public struct GUID : IComparable, IComparable<GUID>, IEquatable<GUID>
    {
        uint m_Value0, m_Value1, m_Value2, m_Value3;

        public GUID(string hexRepresentation)
        {
            m_Value0 = m_Value1 = m_Value2 = m_Value3 = 0;
            if (TryParse(hexRepresentation, out GUID g))
                this = g;
        }

        public bool Empty() => m_Value0 == 0 && m_Value1 == 0 && m_Value2 == 0 && m_Value3 == 0;

        public static GUID Generate()
        {
            GUID g;
            TryParse(Guid.NewGuid().ToString("N"), out g);
            return g;
        }

        /// <summary>32 hex digits (either case); false, with the empty guid, for anything else.</summary>
        public static bool TryParse(string hex, out GUID result)
        {
            result = default;
            if (hex == null || hex.Length != 32)
                return false;
            uint[] v = new uint[4];
            for (int i = 0; i < 32; i++)
            {
                char c = hex[i];
                int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (d < 0)
                    return false;
                v[i / 8] = (v[i / 8] << 4) | (uint)d;
            }
            result.m_Value0 = v[0];
            result.m_Value1 = v[1];
            result.m_Value2 = v[2];
            result.m_Value3 = v[3];
            return true;
        }

        public override string ToString() => $"{m_Value0:x8}{m_Value1:x8}{m_Value2:x8}{m_Value3:x8}";

        public int CompareTo(GUID rhs)
        {
            if (m_Value0 != rhs.m_Value0)
                return m_Value0.CompareTo(rhs.m_Value0);
            if (m_Value1 != rhs.m_Value1)
                return m_Value1.CompareTo(rhs.m_Value1);
            if (m_Value2 != rhs.m_Value2)
                return m_Value2.CompareTo(rhs.m_Value2);
            return m_Value3.CompareTo(rhs.m_Value3);
        }

        public int CompareTo(object obj) => obj is GUID g ? CompareTo(g) : 1;
        public bool Equals(GUID obj) => m_Value0 == obj.m_Value0 && m_Value1 == obj.m_Value1 && m_Value2 == obj.m_Value2 && m_Value3 == obj.m_Value3;
        public override bool Equals(object obj) => obj is GUID g && Equals(g);
        public override int GetHashCode() => (int)(m_Value0 ^ (m_Value1 * 31) ^ (m_Value2 * 131) ^ (m_Value3 * 8191));
        public static bool operator ==(GUID x, GUID y) => x.Equals(y);
        public static bool operator !=(GUID x, GUID y) => !x.Equals(y);
        public static bool operator <(GUID x, GUID y) => x.CompareTo(y) < 0;
        public static bool operator >(GUID x, GUID y) => x.CompareTo(y) > 0;
    }
}
