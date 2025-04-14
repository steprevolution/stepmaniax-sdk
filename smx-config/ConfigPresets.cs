using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace smx_config
{
    class ConfigPresets
    {
        static private string[] Presets = { "low", "normal", "high" };

        static public string GetPreset(SMX.SMXConfig config)
        {
            // If the config we're comparing against is a V1 config, that means the per-panel thresholds
            // weren't set and those fields are ignored (or not supported by) by the firmware.  Sync those
            // thresholds to the unified thresholds before comparing.  That way, we'll correctly match up
            // each preset regardless of what happens to be in the unused per-panel threshold fields.
            // If we don't do this, we won't recognize that the default preset is active because unused
            // fields won't match up.
            if(config.configVersion == 0xFF || config.configVersion < 2)
                SyncUnifiedThresholds(ref config);

            foreach(string Preset in Presets)
            {
                SMX.SMXConfig PresetConfig = SMX.SMXConfig.Create();
                SetPreset(Preset, ref PresetConfig);
                if(SamePreset(config, PresetConfig))
                    return Preset;
            }
            return "";
        }

        // Return true if the config matches, only comparing values that we set in presets.
        static private bool SamePreset(SMX.SMXConfig config1, SMX.SMXConfig config2)
        {
            for(int panel = 0; panel < 9; ++panel)
            {
                if(config1.panelSettings[panel].loadCellLowThreshold != config2.panelSettings[panel].loadCellLowThreshold ||
                    config1.panelSettings[panel].loadCellHighThreshold != config2.panelSettings[panel].loadCellHighThreshold)
                        return false;

                for(int sensor = 0; sensor < 4; ++sensor)
                {
                    if(config1.panelSettings[panel].fsrLowThreshold[sensor] != config2.panelSettings[panel].fsrLowThreshold[sensor] ||
                        config1.panelSettings[panel].fsrHighThreshold[sensor] != config2.panelSettings[panel].fsrHighThreshold[sensor])
                        return false;
                }
            }

            return true;
        }

        static public void SetPreset(string name, ref SMX.SMXConfig config)
        {
            switch(name)
            {
            case "low": SetLowPreset(ref config); return;
            case "normal": SetNormalPreset(ref config); return;
            case "high": SetHighPreset(ref config); return;
            }
        }

        static private void SetPreset(ref SMX.SMXConfig config,
            byte loadCellLow, byte loadCellHigh, byte loadCellLowCenter, byte loadCellHighCenter,
            byte fsrLow, byte fsrHigh, byte fsrLowCenter, byte fsrHighCenter)
        {
            for(int panel = 0; panel < 9; ++panel)
            {
                config.panelSettings[panel].loadCellLowThreshold = loadCellLow;
                config.panelSettings[panel].loadCellHighThreshold = loadCellHigh;
            }

            // Center:
            config.panelSettings[4].loadCellLowThreshold = loadCellLowCenter;
            config.panelSettings[4].loadCellHighThreshold = loadCellHighCenter;

            for(int panel = 0; panel < 9; ++panel)
            {
                for(int sensor = 0; sensor < 4; ++sensor)
                {
                    config.panelSettings[panel].fsrLowThreshold[sensor] = fsrLow;
                    config.panelSettings[panel].fsrHighThreshold[sensor] = fsrHigh;
                }
            }

            // Center:
            for(int sensor = 0; sensor < 4; ++sensor)
            {
                config.panelSettings[4].fsrLowThreshold[sensor] = fsrLowCenter;
                config.panelSettings[4].fsrHighThreshold[sensor] = fsrHighCenter;
            }
        }

        static private void SetHighPreset(ref SMX.SMXConfig config)
        {
            SetPreset(ref config,
                20, 25, 20, 30,
                152, 153, 152, 153);
        }

        static private void SetNormalPreset(ref SMX.SMXConfig config)
        {
            SetPreset(ref config,
                33, 42, 35, 60,
                174, 175, 199, 200);
        }

        static private void SetLowPreset(ref SMX.SMXConfig config)
        {
            SetPreset(ref config,
                70, 80, 100, 120,
                217, 218, 217, 218);
        }

        // Return the extra panels that the given panel's sensitivities control when
        // advanced threshold mode is off.
        static public List<int> GetPanelsToSyncUnifiedThresholds(int fromPanel)
        {
            List<int> result = new List<int>();
            switch(fromPanel)
            {
            case 7: // down (cardinal)
                result.Add(3); // left
                result.Add(5); // right
                break;
            case 2: // up-right (corners)
                result.Add(0); // up-left
                result.Add(6); // down-left
                result.Add(8); // down-right
                break;
            }
            return result;
        }

        // The simplified configuration scheme sets thresholds for up, center, cardinal directions
        // and corners.  Rev1 firmware uses those only.  Copy cardinal directions (down) to the
        // other cardinal directions (except for up, which already had its own setting) and corners
        // to the other corners.
        static private void SyncUnifiedThresholds(ref SMX.SMXConfig config)
        {
            for(int fromPanel = 0; fromPanel < 9; ++fromPanel)
            {
                foreach(int toPanel in GetPanelsToSyncUnifiedThresholds(fromPanel))
                {
                    config.panelSettings[toPanel].loadCellLowThreshold = config.panelSettings[fromPanel].loadCellLowThreshold;
                    config.panelSettings[toPanel].loadCellHighThreshold = config.panelSettings[fromPanel].loadCellHighThreshold;

                    // Do the same for FSR thresholds.
                    for(int sensor = 0; sensor < 4; ++sensor)
                    {
                        config.panelSettings[toPanel].fsrLowThreshold[sensor] = config.panelSettings[fromPanel].fsrLowThreshold[sensor];
                        config.panelSettings[toPanel].fsrHighThreshold[sensor] = config.panelSettings[fromPanel].fsrHighThreshold[sensor];
                    }
                }
            }
        }
    }
}
