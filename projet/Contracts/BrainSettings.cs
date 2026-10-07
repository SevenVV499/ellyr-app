using System;

/*
 * Contrat entre le cerveau (décisions) et le client : les réglages que le cerveau lit et le journal
 * qu'il utilise. Le cerveau n'accède ni à Plugin ni à BepInEx : il passe uniquement par BrainContext,
 * renseigné par le client au démarrage.
 */
namespace EtatJoueurMod
{
    public interface IBrainSettings
    {
        bool RepairEnabled { get; }
        int RepairPercent { get; }
        bool RepairPausesActivity { get; }
        bool FleeEnabled { get; }
        int FleePercent { get; }
        bool FleeCollectEnabled { get; }
        bool OnlyFullHealthTargets { get; }
        bool RaidEnabled { get; }
        bool RaidBossPriority { get; }
    }

    public interface IBrainLog
    {
        void Warning(string message);
        void Error(string message);
    }

    public static class BrainContext
    {
        private static IBrainSettings _settings;
        private static IBrainLog _log;

        // Réglages neutres tant que le client n'a rien renseigné : tout est désactivé.
        private sealed class DefaultSettings : IBrainSettings
        {
            public bool RepairEnabled { get { return false; } }
            public int RepairPercent { get { return 30; } }
            public bool RepairPausesActivity { get { return false; } }
            public bool FleeEnabled { get { return false; } }
            public int FleePercent { get { return 60; } }
            public bool FleeCollectEnabled { get { return false; } }
            public bool OnlyFullHealthTargets { get { return false; } }
            public bool RaidEnabled { get { return false; } }
            public bool RaidBossPriority { get { return true; } }
        }

        private sealed class NullLog : IBrainLog
        {
            public void Warning(string message) { }
            public void Error(string message) { }
        }

        private static readonly IBrainSettings DefaultValues = new DefaultSettings();
        private static readonly IBrainLog NullValue = new NullLog();

        public static IBrainSettings Settings
        {
            get { return _settings ?? DefaultValues; }
            set { _settings = value; }
        }

        public static IBrainLog Log
        {
            get { return _log ?? NullValue; }
            set { _log = value; }
        }
    }
}
