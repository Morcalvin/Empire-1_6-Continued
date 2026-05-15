using FactionColonies.util;
using System;

namespace FactionColonies
{
    public static class MilitaryWindowRegistry
    {
        public delegate MilitaryWindow WindowFactory(MilitaryCustomizationUtil util, FactionFC faction);

        private static WindowFactory _unitsFactory;
        private static WindowFactory _squadsFactory;
        private static WindowFactory _fireSupportFactory;

        private static string _unitsRegistrant;
        private static string _squadsRegistrant;
        private static string _fireSupportRegistrant;

        /* Registration */

        public static void Register(MilitaryWindowSlot slot, WindowFactory factory, string modName = null)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));

            switch (slot)
            {
                case MilitaryWindowSlot.Units:
                    if (_unitsFactory is object)
                        LogUtil.Warning($"MilitaryWindowRegistry: Units window already registered by '{_unitsRegistrant ?? "unknown"}', being overwritten by '{modName ?? "unknown"}'.");
                    _unitsFactory = factory;
                    _unitsRegistrant = modName;
                    break;
                case MilitaryWindowSlot.Squads:
                    if (_squadsFactory is object)
                        LogUtil.Warning($"MilitaryWindowRegistry: Squads window already registered by '{_squadsRegistrant ?? "unknown"}', being overwritten by '{modName ?? "unknown"}'.");
                    _squadsFactory = factory;
                    _squadsRegistrant = modName;
                    break;
                case MilitaryWindowSlot.FireSupport:
                    if (_fireSupportFactory is object)
                        LogUtil.Warning($"MilitaryWindowRegistry: FireSupport window already registered by '{_fireSupportRegistrant ?? "unknown"}', being overwritten by '{modName ?? "unknown"}'.");
                    _fireSupportFactory = factory;
                    _fireSupportRegistrant = modName;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
        }

        public static void Unregister(MilitaryWindowSlot slot)
        {
            switch (slot)
            {
                case MilitaryWindowSlot.Units:
                    _unitsFactory = null;
                    _unitsRegistrant = null;
                    break;
                case MilitaryWindowSlot.Squads:
                    _squadsFactory = null;
                    _squadsRegistrant = null;
                    break;
                case MilitaryWindowSlot.FireSupport:
                    _fireSupportFactory = null;
                    _fireSupportRegistrant = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
        }

        public static void ClearAll()
        {
            _unitsFactory = null;
            _squadsFactory = null;
            _fireSupportFactory = null;
            _unitsRegistrant = null;
            _squadsRegistrant = null;
            _fireSupportRegistrant = null;
        }

        /* Creation */

        public static MilitaryWindow Create(MilitaryWindowSlot slot, MilitaryCustomizationUtil util, FactionFC faction)
        {
            switch (slot)
            {
                case MilitaryWindowSlot.Units:
                    return _unitsFactory is object
                        ? _unitsFactory(util, faction)
                        : new DesignUnitsWindow(util, faction);
                case MilitaryWindowSlot.Squads:
                    return _squadsFactory is object
                        ? _squadsFactory(util, faction)
                        : new DesignSquadsWindow(util);
                case MilitaryWindowSlot.FireSupport:
                    return _fireSupportFactory is object
                        ? _fireSupportFactory(util, faction)
                        : new FireSupportWindow(util);
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
        }

        /* Shorthand creation */

        public static MilitaryWindow CreateUnits(MilitaryCustomizationUtil util, FactionFC faction)
            => Create(MilitaryWindowSlot.Units, util, faction);

        public static MilitaryWindow CreateSquads(MilitaryCustomizationUtil util, FactionFC faction)
            => Create(MilitaryWindowSlot.Squads, util, faction);

        public static MilitaryWindow CreateFireSupport(MilitaryCustomizationUtil util, FactionFC faction)
            => Create(MilitaryWindowSlot.FireSupport, util, faction);
    }
}
