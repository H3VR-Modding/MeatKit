using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MeatKit
{
    public class SimpleVersion : IComparable<SimpleVersion>
    {
        private const string RegexPattern = @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$";

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string Prerelease { get; private set; }
        public string BuildMetadata { get; private set; }

        public static SimpleVersion Parse(string version)
        {
            var match = Regex.Match(version, RegexPattern);
            if (!match.Success) throw new ArgumentException("Provided version is not valid SemVer.", "version");

            return new SimpleVersion
            {
                Major = int.Parse(match.Groups[1].Value),
                Minor = int.Parse(match.Groups[2].Value),
                Patch = int.Parse(match.Groups[3].Value),
                Prerelease = match.Groups[4].Value,
                BuildMetadata = match.Groups[5].Value,
            };
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Major).Append(".").Append(Minor).Append(".").Append(Patch);
            if (!string.IsNullOrEmpty(Prerelease)) sb.Append("-").Append(Prerelease);
            if (!string.IsNullOrEmpty(BuildMetadata)) sb.Append("+").Append(BuildMetadata);
            return sb.ToString();
        }

        public int CompareByPrecedence(SimpleVersion other)
        {
            if (other == null)
                return 1;

            var r = Major.CompareTo(other.Major);
            if (r != 0) return r;

            r = Minor.CompareTo(other.Minor);
            if (r != 0) return r;

            r = Patch.CompareTo(other.Patch);
            if (r != 0) return r;

            return CompareComponent(Prerelease, other.Prerelease, true);
        }

        private static int CompareComponent(string a, string b, bool nonemptyIsLower = false)
        {
            var aEmpty = string.IsNullOrEmpty(a);
            var bEmpty = string.IsNullOrEmpty(b);
            if (aEmpty && bEmpty)
                return 0;

            if (aEmpty)
                return nonemptyIsLower ? 1 : -1;
            if (bEmpty)
                return nonemptyIsLower ? -1 : 1;

            var aComps = a.Split('.');
            var bComps = b.Split('.');

            var minLen = Math.Min(aComps.Length, bComps.Length);
            for (int i = 0; i < minLen; i++)
            {
                var ac = aComps[i];
                var bc = bComps[i];
                int aNum;
                var aIsNum = int.TryParse(ac, out aNum);
                int bNum;
                var bIsNum = int.TryParse(bc, out bNum);
                int r;
                if (aIsNum && bIsNum)
                {
                    r = aNum.CompareTo(bNum);
                    if (r != 0) return r;
                }
                else
                {
                    if (aIsNum)
                        return -1;
                    if (bIsNum)
                        return 1;
                    r = string.CompareOrdinal(ac, bc);
                    if (r != 0)
                        return r;
                }
            }

            return aComps.Length.CompareTo(bComps.Length);
        }
        
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            var other = (SimpleVersion)obj;

            return Major == other.Major
                   && Minor == other.Minor
                   && Patch == other.Patch
                   && string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal)
                   && string.Equals(BuildMetadata, other.BuildMetadata, StringComparison.Ordinal);
        }
        
        public override int GetHashCode()
        {
            unchecked
            {
                int result = Major.GetHashCode();
                result = result * 31 + Minor.GetHashCode();
                result = result * 31 + Patch.GetHashCode();
                result = result * 31 + Prerelease.GetHashCode();
                result = result * 31 + BuildMetadata.GetHashCode();
                return result;
            }
        }

        public int CompareTo(SimpleVersion other)
        {
            return CompareByPrecedence(other);
        }
    }
}