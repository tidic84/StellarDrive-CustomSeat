using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KeySeat
{
    internal enum KeyMode : byte
    {
        Hold,   // the output is on while the key is held
        Toggle, // each press switches it on / off
    }

    /// <summary>One key of a seat and the output it drives.</summary>
    internal sealed class KeyBinding
    {
        public string Name = "";
        public string Path = "";
        public byte Output;
        public KeyMode Mode;
        public float Value = 1f;

        public KeyBinding Clone() => (KeyBinding)MemberwiseClone();

        public string Label => string.IsNullOrWhiteSpace(Name) ? Loc.T("Sortie ", "Output ") + (Output + 1) : Name;
    }

    /// <summary>
    /// The keys of one command seat. They belong to the seat: saved with the world, the same for whoever sits in it.
    /// Several keys may drive the same output; their values add up (e.g. W = +1 and S = -1 on one output).
    /// </summary>
    internal sealed class SeatConfig
    {
        public const int Outputs = 8;
        public const int MaxKeys = 24;
        public const int MaxName = 24, MaxPath = 96;

        public readonly List<KeyBinding> Keys = new List<KeyBinding>();

        /// <summary>A new seat: keys 1 to 8 on outputs 1 to 8, held, value 1.</summary>
        public static SeatConfig Default()
        {
            var c = new SeatConfig();
            for (int i = 0; i < Outputs; i++)
                c.Keys.Add(new KeyBinding { Path = "<Keyboard>/" + (i + 1), Output = (byte)i });
            return c;
        }

        public SeatConfig Clone()
        {
            var c = new SeatConfig();
            foreach (KeyBinding k in Keys) c.Keys.Add(k.Clone());
            return c;
        }

        // ---------------------------------------------------------------- network

        public void Write(BinaryWriter w)
        {
            w.Write((byte)Keys.Count);
            foreach (KeyBinding k in Keys)
            {
                w.Write(k.Name ?? "");
                w.Write(k.Path ?? "");
                w.Write(k.Output);
                w.Write((byte)k.Mode);
                w.Write(k.Value);
            }
        }

        /// <summary>Reads and sanitises (the data may come from any client).</summary>
        public static SeatConfig Read(BinaryReader r)
        {
            var c = new SeatConfig();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                var k = new KeyBinding
                {
                    Name = r.ReadString(),
                    Path = r.ReadString(),
                    Output = r.ReadByte(),
                    Mode = (KeyMode)r.ReadByte(),
                    Value = r.ReadSingle(),
                };
                if (c.Keys.Count < MaxKeys) c.Keys.Add(Sanitize(k));
            }
            return c;
        }

        private static KeyBinding Sanitize(KeyBinding k)
        {
            if (k.Name.Length > MaxName) k.Name = k.Name.Substring(0, MaxName);
            if (k.Path.Length > MaxPath) k.Path = "";
            if (k.Output >= Outputs) k.Output = 0;
            if (k.Mode != KeyMode.Toggle) k.Mode = KeyMode.Hold;
            if (float.IsNaN(k.Value) || float.IsInfinity(k.Value)) k.Value = 0f;
            return k;
        }

        // ---------------------------------------------------------------- save file

        /// <summary>One line per key: output mode value path name (path and name escaped, so no spaces).</summary>
        public IEnumerable<string> ToLines(string prefix)
        {
            if (Keys.Count == 0)
            {
                yield return prefix + " none";
                yield break;
            }
            foreach (KeyBinding k in Keys)
                yield return string.Join(" ", prefix, k.Output, k.Mode, k.Value.ToString("R", CultureInfo.InvariantCulture),
                    Esc(k.Path), Esc(k.Name));
        }

        // "~" stands for an empty field, so every line keeps the same number of words
        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "~" : Uri.EscapeDataString(s);

        private static string Unesc(string s) => s == "~" ? "" : Uri.UnescapeDataString(s);

        /// <summary>Parses the part of a line after the prefix; false for the "none" marker of an emptied seat.</summary>
        public static bool TryParseLine(string[] p, int at, out KeyBinding k)
        {
            k = null;
            if (p.Length < at + 5) return false;
            k = Sanitize(new KeyBinding
            {
                Output = byte.Parse(p[at]),
                Mode = p[at + 1] == nameof(KeyMode.Toggle) ? KeyMode.Toggle : KeyMode.Hold,
                Value = float.Parse(p[at + 2], CultureInfo.InvariantCulture),
                Path = Unesc(p[at + 3]),
                Name = Unesc(p[at + 4]),
            });
            return true;
        }
    }
}
