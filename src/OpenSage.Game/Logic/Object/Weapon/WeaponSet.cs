﻿using OpenSage.Data.Sav;

namespace OpenSage.Logic.Object
{
    public sealed class WeaponSet
    {
        private readonly GameObject _gameObject;
        private readonly Weapon[] _weapons;
        private WeaponTemplateSet _currentWeaponTemplateSet;
        private WeaponSlot _currentWeaponSlot;
        private uint _filledWeaponSlots;
        private WeaponAntiFlags _combinedAntiMask;

        internal Weapon CurrentWeapon => _weapons[(int) _currentWeaponSlot];

        internal WeaponSet(GameObject gameObject)
        {
            _gameObject = gameObject;

            _weapons = new Weapon[WeaponTemplateSet.NumWeaponSlots];
        }

        internal void Update()
        {
            if (!_gameObject.Definition.WeaponSets.TryGetValue(_gameObject.WeaponSetConditions, out var weaponTemplateSet))
            {
                return;
            }

            if (_currentWeaponTemplateSet == weaponTemplateSet)
            {
                return;
            }

            _currentWeaponTemplateSet = weaponTemplateSet;

            _currentWeaponSlot = WeaponSlot.Primary;

            _filledWeaponSlots = 0;
            _combinedAntiMask = WeaponAntiFlags.None;

            for (var i = 0; i < _weapons.Length; i++)
            {
                var weaponTemplate = _currentWeaponTemplateSet.Slots[i]?.Weapon.Value;
                if (weaponTemplate != null)
                {
                    _weapons[i] = new Weapon(_gameObject, weaponTemplate, (WeaponSlot) i, _gameObject.GameContext);

                    _filledWeaponSlots |= (uint) (1 << i);
                    _combinedAntiMask |= weaponTemplate.AntiMask;
                }
            }
        }

        internal void Load(SaveFileReader reader)
        {
            reader.ReadVersion(1);

            // This is the object definition which defined the WeaponSet
            // (either a normal object or DefaultThingTemplate)
            var objectDefinitionName = reader.ReadAsciiString();

            var conditions = reader.ReadBitArray<WeaponSetConditions>();

            _currentWeaponTemplateSet = _gameObject.Definition.WeaponSets[conditions];

            // In Generals there are 3 possible weapons.
            // Later games have up to 5.
            for (var i = 0; i < 3; i++)
            {
                var slotFilled = reader.ReadBoolean();
                if (slotFilled)
                {
                    _weapons[i] = new Weapon(_gameObject, _currentWeaponTemplateSet.Slots[i].Weapon.Value, (WeaponSlot) i, _gameObject.GameContext);
                    _weapons[i].Load(reader);
                }
                else
                {
                    _weapons[i] = null;
                }
            }

            _currentWeaponSlot = reader.ReadEnum<WeaponSlot>();

            var unknown2 = reader.ReadUInt32();

            _filledWeaponSlots = reader.ReadUInt32();
            _combinedAntiMask = reader.ReadEnumFlags<WeaponAntiFlags>();

            var unknown5 = reader.ReadUInt32();

            var unknownBool1 = reader.ReadBoolean();
            var unknownBool2 = reader.ReadBoolean();
        }
    }
}
