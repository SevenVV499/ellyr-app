using System;
using System.Collections.Generic;

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
        private static IPlannerServices _services;
        private static IRaidActions _raidActions;
        private static ISurvivalActions _survivalActions;

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

        // Services neutres tant que le client n'a rien renseigné : aucune confirmation, aucune catégorie.
        private sealed class NullServices : IPlannerServices
        {
            public CollectConfirmationState GetCollectConfirmation(
                EtatJeuSnapshot snapshot, uint netId, string collectibleType,
                DateTime startedAtUtc, long callbackSequenceBaseline, long rewardSequenceBaseline)
            {
                return CollectConfirmationState.None;
            }

            public void GetCollectSequences(out long callbackSequence, out long rewardSequence)
            {
                callbackSequence = 0;
                rewardSequence = 0;
            }

            public bool TryGetWeaponCategory(string name, out TargetCategory category)
            {
                category = default(TargetCategory);
                return false;
            }

            public IReadOnlyList<string> MonsterNames { get { return new string[0]; } }
            public IReadOnlyList<string> NpcNames { get { return new string[0]; } }
        }

        private sealed class NullRaidActions : IRaidActions
        {
            public bool TryEnter(bool petiteRaid) { return false; }
        }

        private sealed class NullSurvivalActions : ISurvivalActions
        {
            public void Repair() { }
        }

        private static readonly IPlannerServices NullServicesValue = new NullServices();
        private static readonly IRaidActions NullRaidActionsValue = new NullRaidActions();
        private static readonly ISurvivalActions NullSurvivalActionsValue = new NullSurvivalActions();
        private static readonly IBrainSettings DefaultValues = new DefaultSettings();
        private static readonly IBrainLog NullValue = new NullLog();

        public static IBrainSettings Settings
        {
            get { return _settings ?? DefaultValues; }
            set { _settings = value; }
        }

        public static IPlannerServices Services
        {
            get { return _services ?? NullServicesValue; }
            set { _services = value; }
        }

        public static IRaidActions RaidActions
        {
            get { return _raidActions ?? NullRaidActionsValue; }
            set { _raidActions = value; }
        }

        public static ISurvivalActions SurvivalActions
        {
            get { return _survivalActions ?? NullSurvivalActionsValue; }
            set { _survivalActions = value; }
        }

        public static IBrainLog Log
        {
            get { return _log ?? NullValue; }
            set { _log = value; }
        }
    }
}
