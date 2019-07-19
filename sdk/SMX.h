#ifndef SMX_H
#define SMX_H

#include <stdint.h>
#include <stddef.h> // for offsetof

#ifdef SMX_EXPORTS
#define SMX_API extern "C" __declspec(dllexport)
#else
#define SMX_API extern "C" __declspec(dllimport)
#endif

struct SMXInfo;
struct SMXConfig;
enum SensorTestMode;
enum PanelTestMode;
enum SMXUpdateCallbackReason;
struct SMXSensorTestModeData;

// All functions are nonblocking.  Getters will return the most recent state.  Setters will
// return immediately and do their work in the background.  No functions return errors, and
// setting data on a pad which isn't connected will have no effect.

// Initialize, and start searching for devices.
//
// UpdateCallback will be called when something happens: connection or disconnection, inputs
// changed, configuration updated, test data updated, etc.  It doesn't specify what's changed,
// and the user should check all state that it's interested in.
//
// This is called asynchronously from a helper thread, so the receiver must be thread-safe.
typedef void SMXUpdateCallback(int pad, SMXUpdateCallbackReason reason, void *pUser);
SMX_API void SMX_Start(SMXUpdateCallback UpdateCallback, void *pUser);

// Shut down and disconnect from all devices.  This will wait for any user callbacks to complete,
// and no user callbacks will be called after this returns.  This must not be called from within
// the update callback.
SMX_API void SMX_Stop();

// Set a function to receive diagnostic logs.  By default, logs are written to stdout.
// This can be called before SMX_Start, so it affects any logs sent during initialization.
typedef void SMXLogCallback(const char *log);
SMX_API void SMX_SetLogCallback(SMXLogCallback callback);

// Get info about a pad.  Use this to detect which pads are currently connected.
SMX_API void SMX_GetInfo(int pad, SMXInfo *info);

// Get a mask of the currently pressed panels.
SMX_API uint16_t SMX_GetInputState(int pad);

// (deprecated) Equivalent to SMX_SetLights2(lightsData, 864).
SMX_API void SMX_SetLights(const char lightData[864]);

// Update the lights.  Both pads are always updated together.  lightData is a list of 8-bit RGB
// colors, one for each LED.
//
// lightDataSize is the number of bytes in lightsData.  This should be 1350 (2 pads * 9 panels *
// 25 lights * 3 RGB colors).  For backwards-compatibility, this can also be 864.
//
// Each panel has lights in the following order:
//
// 00  01  02  03
//    16  17  18
// 04  05  06  07
//   19  20  21
// 08  09  10  11
//   22  23  24
// 12  13  14  15
//
// Panels are in the following order:
//
// 012 9AB
// 345 CDE
// 678 F01
//
// With 18 panels, 25 LEDs per panel and 3 bytes per LED, each light update has 1350 bytes of data.
//
// Lights will update at up to 30 FPS.  If lights data is sent more quickly, a best effort will be
// made to send the most recent lights data available, but the panels won't update more quickly.
//
// The panels will return to automatic lighting if no lights are received for a while, so applications
// controlling lights should send light updates continually, even if the lights aren't changing.
//
// For backwards compatibility, if lightDataSize is 864, the old 4x4-only order is used,
// which simply omits lights 16-24.
SMX_API void SMX_SetLights2(const char *lightData, int lightDataSize);

// By default, the panels light automatically when stepped on.  If a lights command is sent by
// the application, this stops happening to allow the application to fully control lighting.
// If no lights update is received for a few seconds, automatic lighting is reenabled by the
// panels.
//
// SMX_ReenableAutoLights can be called to immediately reenable auto-lighting, without waiting
// for the timeout period to elapse.  Games don't need to call this, since the panels will return
// to auto-lighting mode automatically after a brief period of no updates.
SMX_API void SMX_ReenableAutoLights();

// Get the current controller's configuration.
//
// Return true if a configuration is available.  If false is returned, no panel is connected
// and no data will be set.
SMX_API bool SMX_GetConfig(int pad, SMXConfig *config);

// Update the current controller's configuration.  This doesn't block, and the new configuration will
// be sent in the background.  SMX_GetConfig will return the new configuration as soon as this call
// returns, without waiting for it to actually be sent to the controller.
SMX_API void SMX_SetConfig(int pad, const SMXConfig *config);

// Reset a pad to its original configuration.
SMX_API void SMX_FactoryReset(int pad);

// Request an immediate panel recalibration.  This is normally not necessary, but can be helpful
// for diagnostics.
SMX_API void SMX_ForceRecalibration(int pad);

// Set a sensor test mode and request test data.  This is used by the configuration tool.
SMX_API void SMX_SetTestMode(int pad, SensorTestMode mode);
SMX_API bool SMX_GetTestData(int pad, SMXSensorTestModeData *data);

// Set a panel test mode.  These only appear as debug lighting on the panel and don't
// return data to us.  Lights can't be updated while a panel test mode is active.
// This applies to all connected pads.
SMX_API void SMX_SetPanelTestMode(PanelTestMode mode);

// Return the build version of the DLL, which is based on the git tag at build time.  This
// is only intended for diagnostic logging, and it's also the version we show in SMXConfig.
SMX_API const char *SMX_Version();

// General info about a connected controller.  This can be retrieved with SMX_GetInfo.
struct SMXInfo
{
    // True if we're fully connected to this controller.  If this is false, the other
    // fields won't be set.
    bool m_bConnected = false;

    // This device's serial number.  This can be used to distinguish devices from each
    // other if more than one is connected.  This is a null-terminated string instead
    // of a C++ string for C# marshalling.
    char m_Serial[33];

    // This device's firmware version.
    uint16_t m_iFirmwareVersion;
};

enum SMXUpdateCallbackReason {
    // This is called when a generic state change happens: connection or disconnection, inputs changed,
    // test data updated, etc.  It doesn't specify what's changed.  We simply check the whole state.
    SMXUpdateCallback_Updated,

    // This is called when SMX_FactoryReset completes, indicating that SMX_GetConfig will now return
    // the reset configuration.
    SMXUpdateCallback_FactoryResetCommandComplete
};

// Bits for SMXConfig::flags.
enum SMXConfigFlags {
    // If set, panels will use the pressed animation when pressed, and stepColor
    // is ignored.  If unset, panels will be lit solid using stepColor.
    // masterVersion >= 4.  Previous versions always use stepColor.
    PlatformFlags_AutoLightingUsePressedAnimations = 1 << 0,

    // If set, panels are using FSRs, otherwise load cells.
    PlatformFlags_FSR = 1 << 1,
};
#pragma pack(push, 1)

struct packed_sensor_settings_t {
    // Load cell thresholds:
    uint8_t loadCellLowThreshold;
    uint8_t loadCellHighThreshold;

    // FSR thresholds:
    uint8_t fsrLowThreshold[4];
    uint8_t fsrHighThreshold[4];

    uint16_t combinedLowThreshold;
    uint16_t combinedHighThreshold;
    
    // This must be left unchanged.
    uint16_t reserved;
};

static_assert(sizeof(packed_sensor_settings_t) == 16, "Incorrect packed_sensor_settings_t size");

// The configuration for a connected controller.  This can be retrieved with SMX_GetConfig
// and modified with SMX_SetConfig.
//
// The order and packing of this struct corresponds to the configuration packet sent to
// the master controller, so it must not be changed.
struct SMXConfig
{
    // The firmware version of the master controller.  Where supported (version 2 and up), this
    // will always read back the firmware version.  This will default to 0xFF on version 1, and
    // we'll always write 0xFF here so it doesn't change on that firmware version.
    //
    // We don't need this since we can read the "I" command which also reports the version, but
    // this allows panels to also know the master version.
    uint8_t masterVersion = 0xFF;

    // The version of this config packet.  This can be used by the firmware to know which values
    // have been filled in.  Any values not filled in will always be 0xFF, which can be tested
    // for, but that doesn't work for values where 0xFF is a valid value.  This value is unrelated
    // to the firmware version, and just indicates which fields in this packet have been set.
    // Note that we don't need to increase this any time we add a field, only when it's important
    // that we be able to tell if a field is set or not.
    //
    // Versions:
    // - 0xFF: This is a config packet from before configVersion was added.
    // - 0x00: configVersion added
    // - 0x02: panelThreshold0Low through panelThreshold8High added
    // - 0x03: debounceDelayMs added
    uint8_t configVersion = 0x05;

    // Packed flags (masterVersion >= 4).
    uint8_t flags = 0;

    // Panel thresholds are labelled by their numpad position, eg. Panel8 is up.
    // If m_iFirmwareVersion is 1, Panel7 corresponds to all of up, down, left and
    // right, and Panel2 corresponds to UpLeft, UpRight, DownLeft and DownRight.  For
    // later firmware versions, each panel is configured independently.
    //
    // Setting a value to 0xFF disables that threshold.

    // These are internal tunables and should be left unchanged.
    uint16_t debounceNodelayMilliseconds = 0;
    uint16_t debounceDelayMilliseconds = 0;
    uint16_t panelDebounceMicroseconds = 4000;
    uint8_t autoCalibrationMaxDeviation = 100;
    uint8_t badSensorMinimumDelaySeconds = 15;
    uint16_t autoCalibrationAveragesPerUpdate = 60;
    uint16_t autoCalibrationSamplesPerAverage = 500;

    // The maximum tare value to calibrate to (except on startup).
    uint16_t autoCalibrationMaxTare = 0xFFFF;

    // Which sensors on each panel to enable.  This can be used to disable sensors that
    // we know aren't populated.  This is packed, with four sensors on two pads per byte:
    // enabledSensors[0] & 1 is the first sensor on the first pad, and so on.
    uint8_t enabledSensors[5];

    // How long the master controller will wait for a lights command before assuming the
    // game has gone away and resume auto-lights.  This is in 128ms units.
    uint8_t autoLightsTimeout = 1000/128; // 1 second

    // The color to use for each panel when auto-lighting in master mode.  This doesn't
    // apply when the pads are in autonomous lighting mode (no master), since they don't
    // store any configuration by themselves.  These colors should be scaled to the 0-170
    // range.
    uint8_t stepColor[3*9];

    // The default color to set the platform LED strip to.
    uint8_t platformStripColor[3];

    // Which panels to enable auto-lighting for.  Disabled panels will be unlit.
    // 0x01 = panel 0, 0x02 = panel 1, 0x04 = panel 2, etc.  This only affects
    // the master controller's built-in auto lighting and not lights data send
    // from the SDK.
    uint16_t autoLightPanelMask = 0xFFFF;

    // The rotation of the panel, where 0 is the standard rotation, 1 means the panel is
    // rotated right 90 degrees, 2 is rotated 180 degrees, and 3 is rotated 270 degrees.
    // This value is unused.
    uint8_t panelRotation;

    // Per-panel sensor settings:
    packed_sensor_settings_t panelSettings[9];

    // These are internal tunables and should be left unchanged.
    uint8_t preDetailsDelayMilliseconds = 5;

    // Pad the struct to 250 bytes.  This keeps this struct size from changing
    // as we add fields, so the ABI doesn't change.  Applications should leave
    // any data in here unchanged when calling SMX_SetConfig.
    uint8_t padding[49];
};
#pragma pack(pop)

static_assert(offsetof(SMXConfig, padding) == 201, "Incorrect padding alignment");
static_assert(sizeof(SMXConfig) == 250, "Expected 250 bytes");

// The values (except for Off) correspond with the protocol and must not be changed.
enum SensorTestMode {
    SensorTestMode_Off = 0,
    // Return the raw, uncalibrated value of each sensor.
    SensorTestMode_UncalibratedValues = '0',

    // Return the calibrated value of each sensor.
    SensorTestMode_CalibratedValues = '1',

    // Return the sensor noise value.
    SensorTestMode_Noise = '2',

    // Return the sensor tare value.
    SensorTestMode_Tare = '3',
};

// Data for the current SensorTestMode.  The interpretation of sensorLevel depends on the mode.
struct SMXSensorTestModeData
{
    // If false, sensorLevel[n][*] is zero because we didn't receive a response from that panel.
    bool bHaveDataFromPanel[9];

    int16_t sensorLevel[9][4];
    bool bBadSensorInput[9][4];

    // The DIP switch settings on each panel.  This is used for diagnostics displays.
    int iDIPSwitchPerPanel[9];

    // Bad sensor selection jumper indication for each panel.
    bool iBadJumper[9][4];
};

// The values also correspond with the protocol and must not be changed.
// These are panel-side diagnostics modes.
enum PanelTestMode {
    PanelTestMode_Off = '0',
    PanelTestMode_PressureTest = '1',
};

#endif
