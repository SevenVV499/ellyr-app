using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
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
 * Le total est la somme des variations observées entre deux lectures : un changement de
 * Player (reconnexion) ne fausse donc rien, la première lecture sert seulement de référence.
 * Les valeurs restent d'une session à l'autre ; seule la remise à zéro les efface.
 */
public static class ResourceTracker
{
    public sealed class Counter
    {
        public string Name;
        public string Label;
        public long Total;
        internal long Last;
        internal bool HasLast;
        internal MemberInfo Member;
        internal string Core;
        internal bool Translated;
    }

    private static readonly string[] ExplicitNames =
    {
        "oyuncuAltin", "playerPearl", "oyuncuTecrubePuan", "oyuncuTilsim", "oyuncuAcemiTilsim",
        "oyuncuSandikAnahtari", "oyuncuIcePearlSandikAnahtari"
    };

    private static readonly Dictionary<string, string> KnownLabels = new Dictionary<string, string>
    {
        { "oyuncuAltin", "Or" },
        { "playerPearl", "Perles" },
        { "oyuncuTecrubePuan", "Expérience" },
        { "oyuncuTilsim", "Talisman de lumière" },
        { "oyuncuAcemiTilsim", "Talisman de Behemoth" },
        { "oyuncuSandikAnahtari", "Clés de coffre" },
        { "oyuncuIcePearlSandikAnahtari", "Clés de coffre (Ice Pearl)" },
        { "oyuncuRaidHasar", "Dégâts de Raid" }
    };

    // Libellés de secours (français), utilisés quand aucune clé de la table du jeu ne convient.
    private static readonly Dictionary<string, string> FallbackLabels = new Dictionary<string, string>
    {
        { "oyuncuKristal", "Cristal" },
        { "oyuncuBarut", "Poudre" },
        { "oyuncuAsklepios", "Compétence Asklepios" },
        { "oyuncuProfesyonelKorsan", "Pirate Professionnel" },
        { "oyuncuNormalKorsan", "Pirate Normal" },
        { "oyuncuAcemiKorsan", "Pirate Novice" },
        { "oyuncuKanaSusamis", "Soif de Sang" },
        { "oyuncuKayipAsk", "Amour perdu" },
        { "oyuncuSarapnelYagmuru", "Pluie d'Éclats" },
        { "oyuncuTopGuclendirici", "Noyau de canon" },
        { "oyuncuYavaslatici", "Feu d'Elmo" },
        { "oyuncuRoket", "Feu Céleste" },
        { "oyuncuHizTasi", "Pierre de Vitesse" },
        { "oyuncuKalkan", "Armure en Acier" },
        { "oyuncuSisDuvari", "Mur de Brouillard" },
        { "oyuncuYardimCagrisi", "Appel SOS" },
        { "oyuncuAmulet25k", "Amulette 25k" },
        { "oyuncuAmulet50k", "Amulette 50k" },
        { "oyuncuKartalGozu", "Œil d'Aigle" },
        { "oyuncuHavaiGulle", "Boulet Feu d'Artifice" },
        { "oyuncuKabukKiriciGulle", "Boulet Brise-Coquille" },
        { "oyuncuKalpKiriciGulle", "Boulet Brise-Cœur" },
        { "oyuncuOceanGulle", "Boulet Océan" },
        { "oyuncuGuclendirilmisPatlayanGulle", "Boulet Explosif Renforcé" },
        { "oyuncuPatlayanGulle", "Boulet Explosif" },
        { "oyuncuBuzGulle", "Boulet de glace" },
        { "oyuncuOyukGulle", "Boulet Creux" },
        { "oyuncuAltinZipkin", "Harpon d'Or" },
        { "oyuncuGumusZipkin", "Harpon d'Argent" },
        { "oyuncuInciZipkin", "Harpon de perles" },
        { "oyuncuMicoAltin", "Esclave Niveau 1" },
        { "oyuncuMicoInci", "Esclave Niveau 2" },
        { "oyuncuDumenciInci", "Barreur" },
        { "oyuncuTopcuInci", "Canonnier" }
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
    private static int _labelAttempts;
    private static float _nextLabelAt;

    public static IReadOnlyList<Counter> Counters
    {
        get { return Counters_; }
    }

    public static float ElapsedSeconds
    {
        get { return _startedAt < 0f ? 0f : Time.realtimeSinceStartup - _startedAt; }
    }

    // Libellés : la traduction du jeu (LanguagesManager) quand une clé porte exactement le nom
    // du compteur (ou « <nom>baslik »). Les textes ne sont chargés qu'après la langue : on
    // réessaie quelques fois, puis on garde le libellé de secours.
    private const int MaxLabelAttempts = 6;

    private static void ResolveLabels()
    {
        float now = Time.realtimeSinceStartup;
        List<string> keys = AllLocalizationKeys();
        if (keys.Count < 100)
        {
            _nextLabelAt = now + 10f;
            return;
        }

        _labelAttempts++;
        _nextLabelAt = now + 15f;

        for (int i = 0; i < Counters_.Count; i++)
        {
            Counter counter = Counters_[i];
            // Les libellés français fixés ci-dessus priment sur la table du jeu.
            if (counter.Translated || KnownLabels.ContainsKey(counter.Name)
                || FallbackLabels.ContainsKey(counter.Name))
                continue;

            string bestText = null;
            int bestScore = 0;
            for (int k = 0; k < keys.Count; k++)
            {
                string lower = keys[k].ToLowerInvariant();
                int score = 0;
                if (lower == counter.Core + "baslik" || lower == counter.Core + "başlık")
                    score = 3;
                else if (counter.Core.Length >= 4 && lower.Contains(counter.Core) && lower.Contains("basl")
                    && !HasNoisyToken(lower))
                    score = 2;
                else if (lower == counter.Core)
                    score = 1;
                if (score == 0 || score < bestScore)
                    continue;

                // Un libellé est court : les descriptions longues ne conviennent pas.
                string text = AmmoCatalog.Translate(keys[k]);
                if (text == null || text.Length > 40 || text.IndexOf('\n') >= 0)
                    continue;

                if (score > bestScore || bestText == null || text.Length < bestText.Length)
                {
                    bestScore = score;
                    bestText = text;
                }
            }

            if (bestText != null)
            {
                counter.Label = bestText.Trim();
                counter.Translated = true;
            }
        }
    }

    // Clés de dégâts, quantités, prix, descriptions... : jamais un nom d'objet.
    private static bool HasNoisyToken(string lowerKey)
    {
        string[] tokens =
        {
            "hasar", "adet", "fiyat", "aciklama", "ozellika", "bilgi", "vip", "acik", "odul", "kazan"
        };
        for (int i = 0; i < tokens.Length; i++)
        {
            if (lowerKey.Contains(tokens[i]))
                return true;
        }
        return false;
    }

    private static List<string> AllLocalizationKeys()
    {
        var result = new List<string>();
        foreach (KeyValuePair<string, List<string>> pair in AmmoCatalog.ReadLocalizationKeys())
            result.AddRange(pair.Value);
        return result;
    }

    // Rapport pour la traduction : compteur | libellé actuel | clés contenant son nom = texte du jeu.
    public static string TranslationReport()
    {
        ResolveLabels();
        List<string> keys = AllLocalizationKeys();
        var builder = new StringBuilder();
        for (int i = 0; i < Counters_.Count; i++)
        {
            Counter counter = Counters_[i];
            builder.Append(counter.Name).Append(" | ").Append(counter.Label).Append(" | ");
            int found = 0;
            for (int k = 0; k < keys.Count && found < 8; k++)
            {
                if (keys[k].ToLowerInvariant().IndexOf(counter.Core, StringComparison.Ordinal) < 0)
                    continue;
                string text = AmmoCatalog.Translate(keys[k]);
                builder.Append(found > 0 ? " ; " : string.Empty).Append(keys[k]).Append('=')
                    .Append(text == null ? "?" : text.Replace('\n', ' '));
                found++;
            }
            if (found == 0)
            {
                // Aucune clé ne porte le nom entier : candidats par mot du nom (au moins 5 lettres).
                foreach (string word in Words(counter.Name))
                {
                    int wordFound = 0;
                    for (int k = 0; k < keys.Count && wordFound < 6; k++)
                    {
                        if (keys[k].ToLowerInvariant().IndexOf(word, StringComparison.Ordinal) < 0)
                            continue;
                        string text = AmmoCatalog.Translate(keys[k]);
                        if (text == null || text.Length > 60)
                            continue;
                        builder.Append(wordFound == 0 ? " [" + word + "] " : " ; ").Append(keys[k]).Append('=')
                            .Append(text.Replace('\n', ' '));
                        wordFound++;
                    }
                }
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static List<string> Words(string name)
    {
        string core = name;
        if (core.StartsWith("oyuncu", StringComparison.OrdinalIgnoreCase)
            || core.StartsWith("player", StringComparison.OrdinalIgnoreCase))
            core = core.Substring(6);

        var words = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < core.Length; i++)
        {
            if (i > 0 && char.IsUpper(core[i]) && !char.IsUpper(core[i - 1]) && current.Length > 0)
            {
                if (current.Length >= 5)
                    words.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }
            current.Append(core[i]);
        }
        if (current.Length >= 5)
            words.Add(current.ToString().ToLowerInvariant());
        return words;
    }

    // Liste brute des compteurs repérés (nom du jeu, un par ligne), pour la traduction.
    public static string RawNames()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < Counters_.Count; i++)
            builder.Append(Counters_[i].Name).Append('\n');
        return builder.ToString();
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

        if (_labelAttempts < MaxLabelAttempts && Time.realtimeSinceStartup >= _nextLabelAt)
            ResolveLabels();

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
            if (!TryRead(counter.Member, player, out value))
                continue;

            if (counter.HasLast)
                counter.Total += value - counter.Last;
            counter.Last = value;
            counter.HasLast = true;
        }
    }

    private static bool TryRead(MemberInfo member, Player player, out long value)
    {
        value = 0;
        try
        {
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

                Counters_.Add(new Counter
                {
                    Name = memberName,
                    Label = Label(memberName),
                    Member = member,
                    Core = Key(memberName)
                });
            }

            Counters_.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[ResourceTracker] Découverte des compteurs impossible : " + e);
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

    private static string Label(string name)
    {
        string known;
        if (KnownLabels.TryGetValue(name, out known) || FallbackLabels.TryGetValue(name, out known))
            return known;

        string core = name;
        if (core.StartsWith("oyuncu", StringComparison.OrdinalIgnoreCase)
            || core.StartsWith("player", StringComparison.OrdinalIgnoreCase))
            core = core.Substring(6);

        string label;
        if (core.EndsWith("Gulle", StringComparison.OrdinalIgnoreCase) && core.Length > 5)
            label = "Boulet " + Spaced(core.Substring(0, core.Length - 5));
        else if (core.EndsWith("Zipkin", StringComparison.OrdinalIgnoreCase) && core.Length > 6)
            label = "Harpon " + Spaced(core.Substring(0, core.Length - 6));
        else if (core.EndsWith("Govdesi", StringComparison.OrdinalIgnoreCase) && core.Length > 7)
            label = "Coque " + Spaced(core.Substring(0, core.Length - 7));
        else
            label = Spaced(core);

        // Nom du jeu conservé pour les compteurs non traduits, afin de les reconnaître.
        return label + " (" + name + ")";
    }

    private static string Spaced(string text)
    {
        var builder = new StringBuilder(text.Length + 4);
        for (int i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && !char.IsUpper(text[i - 1]))
                builder.Append(' ');
            builder.Append(text[i]);
        }
        return builder.ToString();
    }
}
