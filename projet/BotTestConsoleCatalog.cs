using System;
using System.Collections.Generic;
using EtatJoueurMod;

internal static class BotTestConsoleCatalog
{
    private sealed class PnjNameFilter
    {
        private readonly Func<string, bool> _isNameAllowed;

        internal PnjNameFilter(Func<string, bool> isNameAllowed)
        {
            _isNameAllowed = isNameAllowed;
        }

        internal bool Allows(PnjInfo pnj)
        {
            return pnj != null && _isNameAllowed(pnj.Nom);
        }
    }

    internal static Func<PnjInfo, bool> CreatePnjFilter(Func<string, bool> isNameAllowed)
    {
        var filter = new PnjNameFilter(isNameAllowed);
        return filter.Allows;
    }

    internal static List<string> BuildDisplayCatalog(
        IReadOnlyList<string> catalog,
        TargetCategory expectedCategory)
    {
        var names = new List<string>(catalog.Count);
        var displayed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < catalog.Count; i++)
        {
            string rawName = catalog[i];
            string name = TargetCatalog.NormalizeName(rawName);
            TargetCategory actualCategory;
            if (string.IsNullOrWhiteSpace(name)
                || !displayed.Add(name)
                || !TargetCatalog.TryGetCategory(name, out actualCategory)
                || actualCategory != expectedCategory)
                continue;

            names.Add(name);
        }

        return names;
    }
}
