namespace MVolt.Rebuild
{
    // Internal NVAPI IDs and buffer versions used by NV Voltelle.
    internal static class PrivateNvApiContracts
    {
        internal const uint PstatesGet = 0x6FF81213;
        internal const uint PstatesSet = 0x0F4DAE6B;
        internal const int PstatesSizeV2 = 0x1CF8;
        internal const uint PstatesVersionV2 = 0x00021CF8;

        internal const uint VoltRailsGetInfo = 0x2C73AFDC;
        internal const uint VoltRailsGetStatus = 0x5D0634EE;
        internal const uint VoltRailsGetControl = 0xA3070DB0;
        internal const uint VoltRailsSetControl = 0x87C55C8A;
        internal const int VoltRailsInfoSizeV2 = 0x184C;
        internal const uint VoltRailsInfoVersionV2 = 0x0002184C;
        internal const int VoltRailsStatusSizeV2 = 0x1620;
        internal const uint VoltRailsStatusVersionV2 = 0x00021620;
        internal const int VoltRailsControlSizeV2 = 0x0AC8;
        internal const uint VoltRailsControlVersionV2 = 0x00020AC8;

        internal const uint AdcDevicesGetInfo = 0x68789E2A;
        internal const uint AdcDevicesGetStatus = 0x43D9B26A;
        internal const int AdcDevicesInfoSizeV2 = 0x09F0;
        internal const uint AdcDevicesInfoVersionV2 = 0x000209F0;
        internal const int AdcDevicesStatusSizeV1 = 0x09C8;
        internal const uint AdcDevicesStatusVersionV1 = 0x000109C8;

        internal const uint PowerMonitorGetInfo = 0xC12EB19E;
        internal const uint PowerMonitorGetStatus = 0xF40238EF;
        internal const int PowerMonitorInfoSizeV3 = 0x0CA8;
        internal const uint PowerMonitorInfoVersionV3 = 0x00030CA8;
        internal const int PowerMonitorStatusSizeV1 = 0x059C;
        internal const uint PowerMonitorStatusVersionV1 = 0x0001059C;

        internal const uint PowerTopologyGetStatus = 0xEDCF624E;
        internal const int PowerTopologyStatusSizeV1 = 0x0048;
        internal const uint PowerTopologyStatusVersionV1 = 0x00010048;
        internal const uint PerfDecreaseInfo = 0x7F7F4600;

        internal const uint VfGetInfo = 0x507B4B59;
        internal const uint VfGetStatus = 0x21537AD4;
        internal const uint VfGetControl = 0x23F1B133;
        internal const uint VfSetControl = 0x0733E009;
        internal const int VfInfoSize = 0x182C;
        internal const uint VfInfoVersionWord = 0x0001182C;
        internal const int VfStatusSizeRtx50 = 0x15B0C;
        internal const uint VfStatusVersionWordRtx50 = 0x00035B0C;
        internal const int VfControlSize = 0x2420;
        internal const uint VfControlVersionWord = 0x00012420;

        internal const uint PowerGetInfo = 0x34206D86;
        internal const uint PowerGetStatus = 0x70916171;
        internal const uint PowerSetStatus = 0xAD95F5ED;
        internal const int PowerInfoSizeV1 = 0x00B8;
        internal const uint PowerInfoVersionV1 = 0x000100B8;
        internal const int PowerStatusSizeV1 = 0x0048;
        internal const uint PowerStatusVersionV1 = 0x00010048;

        internal const uint BoostLockGetStatus = 0xE440B867;
        internal const uint BoostLockSetStatus = 0x39442CFB;
        internal const int BoostLockSizeV2 = 0x030C;
        internal const uint BoostLockVersionV2 = 0x0002030C;

        internal const uint XbarGetInfo = 0x57B5A5DF;
        internal const uint XbarGetControl = 0xF58938F5;
        internal const uint XbarSetControl = 0xD14B69CF;
        internal const uint XbarMeasureFrequency = 0x527FC458;
        internal const int XbarInfoSize = 0x86AC;
        internal const uint XbarInfoVersionWord = 0x000486AC;
        internal const int XbarControlSize = 0x61A4;
        internal const uint XbarControlVersionWord = 0x000261A4;
        internal const int XbarMeasureSize = 0x000C;
        internal const uint XbarMeasureVersionWord = 0x0001000C;

        internal const uint FanCoolersGetInfo = 0xFB85B01E;
        internal const uint FanCoolersGetStatus = 0x35AED5E8;
        internal const uint FanCoolersGetControl = 0x814B209F;
        internal const uint FanCoolersSetControl = 0xA58971A5;
        internal const int FanCoolersInfoSizeV1 = 0x062C;
        internal const uint FanCoolersInfoVersionV1 = 0x0001062C;
        internal const int FanCoolersStatusSizeV1 = 0x06A8;
        internal const uint FanCoolersStatusVersionV1 = 0x000106A8;
        internal const int FanCoolersControlSizeV1 = 0x05AC;
        internal const uint FanCoolersControlVersionV1 = 0x000105AC;
    }
}
