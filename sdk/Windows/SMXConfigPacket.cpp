#include "SMXConfigPacket.h"
#include <stdint.h>
#include <stddef.h>

// The config packet format changed in version 5.  This handles compatibility with
// the old configuration packet.  The config packet in SMX.h matches the new format.
//


#pragma pack(push, 1)
struct OldSMXConfig
{
    uint8_t unused1 = 0xFF, unused2 = 0xFF;
    uint8_t unused3 = 0xFF, unused4 = 0xFF;
    uint8_t unused5 = 0xFF, unused6 = 0xFF;

    uint16_t masterDebounceMilliseconds = 0;
    uint8_t panelThreshold7Low = 0xFF, panelThreshold7High = 0xFF; // was "cardinal"
    uint8_t panelThreshold4Low = 0xFF, panelThreshold4High = 0xFF; // was "center"
    uint8_t panelThreshold2Low = 0xFF, panelThreshold2High = 0xFF; // was "corner"

    uint16_t panelDebounceMicroseconds = 4000;
    uint16_t autoCalibrationPeriodMilliseconds = 1000;
    uint8_t autoCalibrationMaxDeviation = 100;
    uint8_t badSensorMinimumDelaySeconds = 15;
    uint16_t autoCalibrationAveragesPerUpdate = 60;

    uint8_t unused7 = 0xFF, unused8 = 0xFF;

    uint8_t panelThreshold1Low = 0xFF, panelThreshold1High = 0xFF; // was "up"

    uint8_t enabledSensors[5];

    uint8_t autoLightsTimeout = 1000/128; // 1 second

    uint8_t stepColor[3*9];

    uint8_t panelRotation;

    uint16_t autoCalibrationSamplesPerAverage = 500;

    uint8_t masterVersion = 0xFF;
    uint8_t configVersion = 0x03;

    uint8_t unused9[10];
    uint8_t panelThreshold0Low, panelThreshold0High;
    uint8_t panelThreshold3Low, panelThreshold3High;
    uint8_t panelThreshold5Low, panelThreshold5High;
    uint8_t panelThreshold6Low, panelThreshold6High;
    uint8_t panelThreshold8Low, panelThreshold8High;

    uint16_t debounceDelayMilliseconds = 0;

    uint8_t padding[164];
};
#pragma pack(pop)
static_assert(offsetof(OldSMXConfig, padding) == 86, "Incorrect padding alignment");
static_assert(sizeof(OldSMXConfig) == 250, "Expected 250 bytes");

void ConvertToNewConfig(const vector<uint8_t> &oldConfigData, SMXConfig &newConfig)
{
    // Copy data in its order within OldSMXConfig.  This lets us easily stop at each
    // known packet version.  Any fields that aren't present in oldConfigData will be
    // left at their default values in SMXConfig.
    const OldSMXConfig &oldConfig = (OldSMXConfig &) *oldConfigData.data();

    newConfig.debounceNodelayMilliseconds = oldConfig.masterDebounceMilliseconds;

    newConfig.panelSettings[7].loadCellLowThreshold = oldConfig.panelThreshold7Low;
    newConfig.panelSettings[4].loadCellLowThreshold = oldConfig.panelThreshold4Low;
    newConfig.panelSettings[2].loadCellLowThreshold = oldConfig.panelThreshold2Low;

    newConfig.panelSettings[7].loadCellHighThreshold = oldConfig.panelThreshold7High;
    newConfig.panelSettings[4].loadCellHighThreshold = oldConfig.panelThreshold4High;
    newConfig.panelSettings[2].loadCellHighThreshold = oldConfig.panelThreshold2High;

    newConfig.panelDebounceMicroseconds = oldConfig.panelDebounceMicroseconds;
    newConfig.autoCalibrationMaxDeviation = oldConfig.autoCalibrationMaxDeviation;
    newConfig.badSensorMinimumDelaySeconds = oldConfig.badSensorMinimumDelaySeconds;
    newConfig.autoCalibrationAveragesPerUpdate = oldConfig.autoCalibrationAveragesPerUpdate;

    newConfig.panelSettings[1].loadCellLowThreshold = oldConfig.panelThreshold1Low;
    newConfig.panelSettings[1].loadCellHighThreshold = oldConfig.panelThreshold1High;

    memcpy(newConfig.enabledSensors, oldConfig.enabledSensors, sizeof(newConfig.enabledSensors));
    newConfig.autoLightsTimeout = oldConfig.autoLightsTimeout;
    memcpy(newConfig.stepColor, oldConfig.stepColor, sizeof(newConfig.stepColor));
    newConfig.panelRotation = oldConfig.panelRotation;
    newConfig.autoCalibrationSamplesPerAverage = oldConfig.autoCalibrationSamplesPerAverage;

    if(oldConfig.configVersion == 0xFF)
        return;

    newConfig.masterVersion = oldConfig.masterVersion;
    newConfig.configVersion = oldConfig.configVersion;

    if(oldConfig.configVersion < 2)
        return;

    newConfig.panelSettings[0].loadCellLowThreshold = oldConfig.panelThreshold0Low;
    newConfig.panelSettings[3].loadCellLowThreshold = oldConfig.panelThreshold3Low;
    newConfig.panelSettings[5].loadCellLowThreshold = oldConfig.panelThreshold5Low;
    newConfig.panelSettings[6].loadCellLowThreshold = oldConfig.panelThreshold6Low;
    newConfig.panelSettings[8].loadCellLowThreshold = oldConfig.panelThreshold8Low;

    newConfig.panelSettings[0].loadCellHighThreshold = oldConfig.panelThreshold0High;
    newConfig.panelSettings[3].loadCellHighThreshold = oldConfig.panelThreshold3High;
    newConfig.panelSettings[5].loadCellHighThreshold = oldConfig.panelThreshold5High;
    newConfig.panelSettings[6].loadCellHighThreshold = oldConfig.panelThreshold6High;
    newConfig.panelSettings[8].loadCellHighThreshold = oldConfig.panelThreshold8High;

    if(oldConfig.configVersion < 3)
        return;

    newConfig.debounceDelayMilliseconds = oldConfig.debounceDelayMilliseconds;
}

// oldConfigData contains the data we're replacing.  Any fields that exist in the old
// config format and not the new one will be left unchanged.
void ConvertToOldConfig(const SMXConfig &newConfig, vector<uint8_t> &oldConfigData)
{
    OldSMXConfig &oldConfig = (OldSMXConfig &) *oldConfigData.data();

    // We don't need to check configVersion here.  It's safe to set all fields in
    // the output config packet.  If oldConfigData isn't 128 bytes, extend it.
    if(oldConfigData.size() < 128)
        oldConfigData.resize(128, 0xFF);

    oldConfig.masterDebounceMilliseconds = newConfig.debounceNodelayMilliseconds;

    oldConfig.panelThreshold7Low = newConfig.panelSettings[7].loadCellLowThreshold;
    oldConfig.panelThreshold4Low = newConfig.panelSettings[4].loadCellLowThreshold;
    oldConfig.panelThreshold2Low = newConfig.panelSettings[2].loadCellLowThreshold;

    oldConfig.panelThreshold7High = newConfig.panelSettings[7].loadCellHighThreshold;
    oldConfig.panelThreshold4High = newConfig.panelSettings[4].loadCellHighThreshold;
    oldConfig.panelThreshold2High = newConfig.panelSettings[2].loadCellHighThreshold;

    oldConfig.panelDebounceMicroseconds = newConfig.panelDebounceMicroseconds;
    oldConfig.autoCalibrationMaxDeviation = newConfig.autoCalibrationMaxDeviation;
    oldConfig.badSensorMinimumDelaySeconds = newConfig.badSensorMinimumDelaySeconds;
    oldConfig.autoCalibrationAveragesPerUpdate = newConfig.autoCalibrationAveragesPerUpdate;

    oldConfig.panelThreshold1Low = newConfig.panelSettings[1].loadCellLowThreshold;
    oldConfig.panelThreshold1High = newConfig.panelSettings[1].loadCellHighThreshold;

    memcpy(oldConfig.enabledSensors, newConfig.enabledSensors, sizeof(newConfig.enabledSensors));
    oldConfig.autoLightsTimeout = newConfig.autoLightsTimeout;
    memcpy(oldConfig.stepColor, newConfig.stepColor, sizeof(newConfig.stepColor));
    oldConfig.panelRotation = newConfig.panelRotation;
    oldConfig.autoCalibrationSamplesPerAverage = newConfig.autoCalibrationSamplesPerAverage;

    oldConfig.masterVersion = newConfig.masterVersion;
    oldConfig.configVersion= newConfig.configVersion;

    oldConfig.panelThreshold0Low = newConfig.panelSettings[0].loadCellLowThreshold;
    oldConfig.panelThreshold3Low = newConfig.panelSettings[3].loadCellLowThreshold;
    oldConfig.panelThreshold5Low = newConfig.panelSettings[5].loadCellLowThreshold;
    oldConfig.panelThreshold6Low = newConfig.panelSettings[6].loadCellLowThreshold;
    oldConfig.panelThreshold8Low = newConfig.panelSettings[8].loadCellLowThreshold;

    oldConfig.panelThreshold0High = newConfig.panelSettings[0].loadCellHighThreshold;
    oldConfig.panelThreshold3High = newConfig.panelSettings[3].loadCellHighThreshold;
    oldConfig.panelThreshold5High = newConfig.panelSettings[5].loadCellHighThreshold;
    oldConfig.panelThreshold6High = newConfig.panelSettings[6].loadCellHighThreshold;
    oldConfig.panelThreshold8High = newConfig.panelSettings[8].loadCellHighThreshold;

    oldConfig.debounceDelayMilliseconds = newConfig.debounceDelayMilliseconds;
}
