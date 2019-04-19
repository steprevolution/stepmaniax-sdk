#ifndef SMXConfigPacket_h
#define SMXConfigPacket_h

#include <vector>
using namespace std;

#include "../SMX.h"

void ConvertToNewConfig(const vector<uint8_t> &oldConfig, SMXConfig &newConfig);
void ConvertToOldConfig(const SMXConfig &newConfig, vector<uint8_t> &oldConfigData);

#endif
