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
                SMX.SMXConfig PresetConfig = config;
                SetPreset(Preset, ref PresetConfig);
                if(SamePreset(config, PresetConfig))
                    return Preset;
            }
            return "";
        }

        // Return true if the config matches, only comparing values that we set in presets.
        static private bool SamePreset(SMX.SMXConfig config1, SMX.SMXConfig config2)
        {
            // These aren't arrays for compatibility reasons.
            if( config1.panelThreshold0High != config2.panelThreshold0High ||
                config1.panelThreshold1High != config2.panelThreshold1High ||
                config1.panelThreshold2High != config2.panelThreshold2High ||
                config1.panelThreshold3High != config2.panelThreshold3High ||
                config1.panelThreshold4High != config2.panelThreshold4High ||
                config1.panelThreshold5High != config2.panelThreshold5High ||
                config1.panelThreshold6High != config2.panelThreshold6High ||
                config1.panelThreshold7High != config2.panelThreshold7High ||
                config1.panelThreshold8High != config2.panelThreshold8High)
                return false;
            if( config1.panelThreshold0Low != config2.panelThreshold0Low ||
                config1.panelThreshold1Low != config2.panelThreshold1Low ||
                config1.panelThreshold2Low != config2.panelThreshold2Low ||
                config1.panelThreshold3Low != config2.panelThreshold3Low ||
                config1.panelThreshold4Low != config2.panelThreshold4Low ||
                config1.panelThreshold5Low != config2.panelThreshold5Low ||
                config1.panelThreshold6Low != config2.panelThreshold6Low ||
                config1.panelThreshold7Low != config2.panelThreshold7Low ||
                config1.panelThreshold8Low != config2.panelThreshold8Low)
                return false;
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

        static private void SetHighPreset(ref SMX.SMXConfig config)
        {
            config.panelThreshold7Low =      // cardinal
            config.panelThreshold1Low =      // up
            config.panelThreshold2Low = 20;  // corner
            config.panelThreshold7High =     // cardinal
            config.panelThreshold1High =     // up
            config.panelThreshold2High = 25; // corner

            config.panelThreshold4Low = 20; // center
            config.panelThreshold4High = 30;
            SyncUnifiedThresholds(ref config);
        }

        static private void SetNormalPreset(ref SMX.SMXConfig config)
        {
            config.panelThreshold7Low =      // cardinal
            config.panelThreshold1Low =      // up
            config.panelThreshold2Low = 33;  // corner
            config.panelThreshold7High =     // cardinal
            config.panelThreshold1High =     // up
            config.panelThreshold2High = 42; // corner

            config.panelThreshold4Low = 35;  // center
            config.panelThreshold4High = 60;
            SyncUnifiedThresholds(ref config);
        }

        static private void SetLowPreset(ref SMX.SMXConfig config)
        {
            config.panelThreshold7Low =      // cardinal
            config.panelThreshold1Low =      // up
            config.panelThreshold2Low = 70;  // corner
            config.panelThreshold7High =     // cardinal
            config.panelThreshold1High =     // up
            config.panelThreshold2High = 80; // corner

            config.panelThreshold4Low = 100; // center
            config.panelThreshold4High = 120;
            SyncUnifiedThresholds(ref config);
        }

        // The simplified configuration scheme sets thresholds for up, center, cardinal directions
        // and corners.  Rev1 firmware uses those only.  Copy cardinal directions (down) to the
        // other cardinal directions (except for up, which already had its own setting) and corners
        // to the other corners.
        static public void SyncUnifiedThresholds(ref SMX.SMXConfig config)
        {
            // left = right = down (cardinal)
            config.panelThreshold3Low = config.panelThreshold5Low = config.panelThreshold7Low;
            config.panelThreshold3High = config.panelThreshold5High = config.panelThreshold7High;

            // UL = DL = DR = UR (corners)
            config.panelThreshold0Low = config.panelThreshold6Low = config.panelThreshold8Low = config.panelThreshold2Low;
            config.panelThreshold0High = config.panelThreshold6High = config.panelThreshold8High = config.panelThreshold2High;

            // Do the same for FSR thresholds.
            config.individualPanelFSRLow[3] = config.individualPanelFSRLow[5] = config.individualPanelFSRLow[7];
            config.individualPanelFSRHigh[3] = config.individualPanelFSRHigh[5] = config.individualPanelFSRHigh[7];
            config.individualPanelFSRLow[0] = config.individualPanelFSRLow[6] = config.individualPanelFSRLow[8] = config.individualPanelFSRLow[2];
            config.individualPanelFSRHigh[0] = config.individualPanelFSRHigh[6] = config.individualPanelFSRHigh[8] = config.individualPanelFSRHigh[2];
        }

        // Return true if the panel thresholds are already synced, so SyncUnifiedThresholds would
        // have no effect.
        static public bool AreUnifiedThresholdsSynced(SMX.SMXConfig config)
        {
            SMX.SMXConfig config2 = config;
            SyncUnifiedThresholds(ref config2);
            return SamePreset(config, config2);
        }
    }
}
