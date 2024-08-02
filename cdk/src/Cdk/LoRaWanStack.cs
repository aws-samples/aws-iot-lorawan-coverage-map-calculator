using Amazon.CDK;
using Constructs;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.IoTWireless;
using System;

namespace Cdk
{
    /// <summary>
    /// This stack creates the AWS IoT Core for LoRaWAN ServiceProfile and Device Profile to be used by the 
    /// field tester devices to connect using the roaming partner 
    /// </summary>
    public class LoRaWanStack : Stack
    {
        internal LoRaWanStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // Define Service Profile
            new CfnServiceProfile(this, "LoRaWANServiceProfile", new CfnServiceProfileProps
            {
                Name = "Roaming Enabled",
                LoRaWan = new CfnServiceProfile.LoRaWANServiceProfileProperty
                {
                    AddGwMetadata = true,
                    RaAllowed = true,
                    PrAllowed = true
                }
            });

            // Define Device Profile
            new CfnDeviceProfile(this, "DeviceProfile_EU868_A_OTAA", new CfnDeviceProfileProps
            {
                Name = "EU868-A-OTAA",
                LoRaWan = new CfnDeviceProfile.LoRaWANDeviceProfileProperty
                {
                    SupportsClassB = false,
                    SupportsClassC = false,
                    MacVersion = "1.0.3",
                    RegParamsRevision = "RP002-1.0.1",
                    RxDelay1 = 1,
                    RxDrOffset1 = 0,
                    RxDataRate2 = 0,
                    RxFreq2 = 8695250,
                    FactoryPresetFreqsList = new double[] { 8681000, 8683000, 8685000 },
                    MaxEirp = 5,
                    MaxDutyCycle = 0,
                    RfRegion = "EU868",
                    SupportsJoin = true,
                    Supports32BitFCnt = true
                }
            });            
        }
    }
}
