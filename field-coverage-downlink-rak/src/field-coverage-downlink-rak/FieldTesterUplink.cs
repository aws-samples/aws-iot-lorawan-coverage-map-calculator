using System;
using Amazon.IoT;

namespace field_coverage_lambda
{   
    public class FieldTesterUplink {
        public string MessageId { get; set; }

        public string WirelessDeviceType {get;set;}

        public string WirelessDeviceId { get; set; }
        public string PayloadData { get; set; }
        public WirelessMetadata WirelessMetadata { get; set; } 
        public DecodedUplink DecodedUplink { get; set; } 
    }
    
    public class DecodedUplink 
    {
        public bool IsValid { get; set; }
        
        public Position Position { get; set; } 

        public List<object> Events {get;set;}

        public PositionTypeEnum PositionType {get;set;}

        public double? Battery { get; set; }
        public long Timestamp { get; set; }
        public string ErrorMessage { get; set; }
    }


    public class WirelessMetadata {
        public LoRaWAN LoRaWAN { get; set; } 
    }



    public class GatewayBase
    {
        public double Rssi { get; set; }
        public double Snr { get; set; }


        //those details are only available for private gateways normally
        //but keep this into the base class as in case will be also available for 
        //public gateways (thanks to specific agreements)        
        public Position? GatewayPosition { get; set; }
        public double? DistanceFromDevice { get; set; }
        public PositionTypeEnum PositionType { get; set; }
    }

    
    public class Position {
        public double Longitude { get; set; }
        public double Latitude { get; set; }
    }

    [Flags]
    public enum PositionTypeEnum
    {
        Unknown = 0,
        GPS = 1,
        Indoor = 2,        
        LastKnown = 4,
        Manual = 8
    }

    public class PrivateGateway : GatewayBase 
    {
        public string GatewayEui { get; set; }
    }

    public class PublicGateway : GatewayBase
    {
        public PublicGateway()
        {
            PositionType = PositionTypeEnum.Unknown;
        }

        public bool DlAllowed { get; set; }
        public string Id { get; set; }
        public string ProviderNetId { get; set; }
        public string RfRegion { get; set; }
    }

    public class LoRaWAN {

        public LoRaWAN()
        {
            IsPublicNetwork = false;
        }


        public bool ADR { get; set; }
        public int? Bandwidth { get; set; }
        public bool ClassB { get; set; }
        public string? CodeRate { get; set; }
        public string DataRate { get; set; }
        public string DevAddr { get; set; }
        public string DevEui { get; set; }
        public int FCnt { get; set; }
        public int FOptLen { get; set; }
        public int FPort { get; set; }
        public string Frequency { get; set; }

        public bool IsPublicNetwork { get; set; }

        public IList<PrivateGateway> Gateways { get; set; }

        public IList<PublicGateway> PublicGateways { get; set; }

        public string MIC { get; set; }
        public string MType { get; set; }
        public string Major { get; set; }
        public string? Modulation { get; set; }
        public bool? PolarizationInversion { get; set; }
        public int SpreadingFactor { get; set; }
        public DateTime Timestamp { get; set; }
    }

}

