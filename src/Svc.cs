using System;
using System.Collections.Generic;
using Core.Services;
using Players.Services;
using Ships.Services;
using UnityEngine.Localization.Settings;

namespace KeySeat
{
    /// <summary>Per-scene cache over the game's ServiceLocator (which scans tagged objects on every call).</summary>
    internal static class Svc
    {
        private static readonly Dictionary<Type, object> Cache = new Dictionary<Type, object>();

        public static void Clear() => Cache.Clear();

        public static T Get<T>() where T : class
        {
            if (Cache.TryGetValue(typeof(T), out object o) && o is T t && !(o is UnityEngine.Object uo && uo == null))
                return t;
            T found = ServiceLocator.GetService<T>();
            if (found != null) Cache[typeof(T)] = found;
            return found;
        }

        public static ShipsClientTracker ShipsClient => Get<ShipsClientTracker>();
        public static ShipsServerTracker ShipsServer => Get<ShipsServerTracker>();
        public static PlayersClientTracker PlayersClient => Get<PlayersClientTracker>();
    }

    /// <summary>French or English, following the language picked in the game options.</summary>
    internal static class Loc
    {
        public static bool French
        {
            get
            {
                try
                {
                    string code = LocalizationSettings.SelectedLocale?.Identifier.Code;
                    return code != null && code.StartsWith("fr");
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string T(string fr, string en) => French ? fr : en;
    }
}
