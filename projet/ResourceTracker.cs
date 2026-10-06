using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using EtatJoueurMod;
using UnityEngine;

/*
 * Suivi des ressources : variation nette de chaque compteur numérique du joueur (or, perles,
 * boulets, harpons, coques, bonus, talismans...) depuis la dernière remise à zéro.
 *
 * Les compteurs sont découverts automatiquement par réflexion sur Player : propriété ou champ
 * int/long dont le nom commence par « oyuncu » ou « player » et qui possède un setter
 * « SetOyuncu... / SetPlayer... » (le jeu met ces compteurs à jour par ces setters), plus
 * quelques noms explicites dont le setter a une orthographe différente.
 *
 * Les compteurs sont affichés sous leur nom brut du jeu : aucune traduction, aucune table de langue.
 *
 * Le total est la somme des variations observées entre deux lectures : un changement de
 * Player (reconnexion) ne fausse donc rien, la première lecture sert seulement de référence.
 * Les valeurs restent d'une session à l'autre ; seule la remise à zéro les efface.
 */
public static class ResourceTracker
{
    public sealed class Counter
    {
        public string Name;
        public long Total;
        internal long Last;
        internal bool HasLast;
        internal MemberInfo Member;

        // Lecteurs typés (sans conversion en objet) créés à la découverte quand c'est possible.
        internal Func<Player, int> ReadInt;
        internal Func<Player, long> ReadLong;

        // Textes d'affichage recalculés seulement quand la valeur change (ou toutes les 5 s pour le rythme).
        private long _totalTextFor = long.MinValue;
        private string _totalText = string.Empty;
        private long _perHourFor = long.MinValue;
        private int _perHourBucket = -1;
        private string _perHourText = string.Empty;

        public string TotalText
        {
            get
            {
                if (_totalTextFor != Total)
                {
                    _totalTextFor = Total;
                    _totalText = FormatSigned(Total);
                }
                return _totalText;
            }
        }

        public string PerHourText(float elapsedSeconds)
        {
            if (elapsedSeconds < 60f)
                return "-";

            int bucket = (int)(elapsedSeconds / 5f);
            if (_perHourFor != Total || _perHourBucket != bucket)
            {
                _perHourFor = Total;
                _perHourBucket = bucket;
                _perHourText = FormatSigned((long)(Total / (elapsedSeconds / 3600f))) + " /h";
            }
            return _perHourText;
        }
    }

    public static string FormatSigned(long value)
    {
        string digits = Math.Abs(value).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        return value > 0 ? "+" + digits : value < 0 ? "-" + digits : digits;
    }

    private static readonly string[] ExplicitNames =
    {
        "oyuncuAltin", "playerPearl", "oyuncuTecrubePuan", "oyuncuTilsim", "oyuncuAcemiTilsim",
        "oyuncuSandikAnahtari", "oyuncuIcePearlSandikAnahtari"
    };

    // Compteurs qui ne sont pas des ressources (identifiants, états, emplacements, progression).
    private static bool IsExcluded(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.EndsWith("id", StringComparison.Ordinal))
            return true;
        string[] tokens =
        {
            "durumu", "bankontrol", "sunucutahtasi", "slot", "kaleyetenek", "donanilmis",
            "yuvasi", "ilerleme", "sirasi", "haritapak", "tasinan", "teslim", "filoseviye"
        };
        for (int i = 0; i < tokens.Length; i++)
        {
            if (lower.Contains(tokens[i]))
                return true;
        }
        return false;
    }

    private static readonly List<Counter> Counters_ = new List<Counter>();
    private static bool _discovered;
    private static IntPtr _lastPlayer;
    private static float _startedAt = -1f;

    public static IReadOnlyList<Counter> Counters
    {
        get { return Counters_; }
    }

    public static float ElapsedSeconds
    {
        get { return _startedAt < 0f ? 0f : Time.realtimeSinceStartup - _startedAt; }
    }

    // Remise à zéro demandée par l'utilisateur : les variations repartent de 0.
    public static void Reset()
    {
        for (int i = 0; i < Counters_.Count; i++)
            Counters_[i].Total = 0;
        _startedAt = Time.realtimeSinceStartup;
    }

    // Appelé à chaque instantané avec le joueur local.
    public static void Sample(Player player)
    {
        if (player == null)
            return;

        if (!_discovered)
            Discover();
        if (_startedAt < 0f)
            _startedAt = Time.realtimeSinceStartup;

        // Nouveau Player (reconnexion, changement de scène) : les lectures qui suivent ne
        // servent que de nouvelle référence, sans ajouter de variation.
        if (player.Pointer != _lastPlayer)
        {
            _lastPlayer = player.Pointer;
            for (int i = 0; i < Counters_.Count; i++)
                Counters_[i].HasLast = false;
        }

        for (int i = 0; i < Counters_.Count; i++)
        {
            Counter counter = Counters_[i];
            long value;
            if (!TryRead(counter, player, out value))
                continue;

            if (counter.HasLast)
                counter.Total += value - counter.Last;
            counter.Last = value;
            counter.HasLast = true;
        }
    }

    private static bool TryRead(Counter counter, Player player, out long value)
    {
        value = 0;
        try
        {
            if (counter.ReadInt != null)
            {
                value = counter.ReadInt(player);
                return true;
            }
            if (counter.ReadLong != null)
            {
                value = counter.ReadLong(player);
                return true;
            }

            MemberInfo member = counter.Member;
            object raw;
            PropertyInfo property = member as PropertyInfo;
            if (property != null)
                raw = property.GetValue(player, null);
            else
                raw = ((FieldInfo)member).GetValue(player);

            if (raw == null)
                return false;
            value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Discover()
    {
        _discovered = true;
        try
        {
            Type type = typeof(Player);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var setterKeys = new HashSet<string>();
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                string name = method.Name;
                if (!name.StartsWith("Set", StringComparison.Ordinal) || name.Length <= 3)
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1
                    || parameters[0].ParameterType != typeof(int) && parameters[0].ParameterType != typeof(long))
                    continue;
                setterKeys.Add(Key(name.Substring(3)));
            }

            var seen = new HashSet<string>();
            foreach (MemberInfo member in type.GetMembers(flags))
            {
                Type valueType;
                PropertyInfo property = member as PropertyInfo;
                FieldInfo field = member as FieldInfo;
                if (property != null)
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0)
                        continue;
                    valueType = property.PropertyType;
                }
                else if (field != null)
                {
                    valueType = field.FieldType;
                }
                else
                {
                    continue;
                }

                if (valueType != typeof(int) && valueType != typeof(long))
                    continue;

                string memberName = member.Name;
                bool isExplicit = Array.IndexOf(ExplicitNames, memberName) >= 0;
                if (!isExplicit)
                {
                    if (!StartsWithAny(memberName, "oyuncu", "player"))
                        continue;
                    if (!setterKeys.Contains(Key(memberName)))
                        continue;
                }

                if (!seen.Add(memberName) || IsExcluded(memberName))
                    continue;

                var counter = new Counter
                {
                    Name = memberName,
                    Member = member
                };
                AttachTypedReader(counter, property, valueType);
                Counters_.Add(counter);
            }

            Counters_.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[ResourceTracker] Découverte des compteurs impossible : " + e);
        }
    }

    // Lecteur typé via le getter de la propriété : évite la conversion en objet à chaque lecture.
    // En cas d'échec, la lecture par réflexion reste utilisée.
    private static void AttachTypedReader(Counter counter, PropertyInfo property, Type valueType)
    {
        if (property == null)
            return;

        try
        {
            MethodInfo getter = property.GetGetMethod();
            if (getter == null || getter.IsStatic)
                return;

            if (valueType == typeof(int))
                counter.ReadInt = (Func<Player, int>)Delegate.CreateDelegate(typeof(Func<Player, int>), getter);
            else if (valueType == typeof(long))
                counter.ReadLong = (Func<Player, long>)Delegate.CreateDelegate(typeof(Func<Player, long>), getter);
        }
        catch
        {
            counter.ReadInt = null;
            counter.ReadLong = null;
        }
    }

    private static bool StartsWithAny(string value, string a, string b)
    {
        return value.StartsWith(a, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    // Clé de rapprochement entre un compteur et son setter : minuscules, sans préfixe.
    private static string Key(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.StartsWith("oyuncu", StringComparison.Ordinal))
            return lower.Substring(6);
        if (lower.StartsWith("player", StringComparison.Ordinal))
            return lower.Substring(6);
        return lower;
    }
}
