using System;
using NetTopologySuite.Geometries;

using H3;
using H3.Algorithms;
using H3.Extensions;

using NetTopologySuite.Features;
using NetTopologySuite.IO;
using NetTopologySuite;
using System.Globalization;

namespace field_coverage_lambda
{
    public class FieldTestResult : FieldTesterUplink
    {
        /// <summary>
        /// empty ctor for deseriaization
        /// </summary>
        public FieldTestResult()
        {

        }

        public FieldTestResult(FieldTesterUplink uplink)
        {            
            this.WirelessDeviceType = uplink.WirelessDeviceType;
            this.DecodedUplink = uplink.DecodedUplink;
            this.MessageId = uplink.MessageId;
            this.PayloadData = uplink.PayloadData;
            this.WirelessDeviceId = uplink.WirelessDeviceId;
            this.WirelessMetadata = uplink.WirelessMetadata;


            this.Year = this.WirelessMetadata.LoRaWAN.Timestamp.Year;
            this.Week = getWeekOfYear( this.WirelessMetadata.LoRaWAN.Timestamp );
        }

        public string Operator { get; set; }

        //e.g. LoRaWAN or NB-IoT
        public string Technology { get; set; }

        public string DeviceName { get; set; }     
        
        public int Year { get; set; }
        public int Week { get; set; }
        

        public LoRaWANFieldTestResultStatistics Statistics { get; set; }

        public H3Hashes H3 {get;set;}



        //compute the statistics based on Gateways info (where available)
        public void ComputeStatistics()
        {
            this.Statistics = new LoRaWANFieldTestResultStatistics();
            
            this.Statistics.FieldTesterPositionAvailable = false;

            GeometryFactory geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);

            //this is the field tester point (geography with WGS-84 coordinates)
            Point fieldTesterPoint = null;

            if ( this.DecodedUplink.PositionType == PositionTypeEnum.GPS ||
                 this.DecodedUplink.PositionType == PositionTypeEnum.LastKnown ||
                 this.DecodedUplink.PositionType == PositionTypeEnum.Indoor ||
                 this.DecodedUplink.PositionType == PositionTypeEnum.Manual )
            {
                fieldTesterPoint = geometryFactory.CreatePoint( new Coordinate( this.DecodedUplink.Position.Longitude,
                    this.DecodedUplink.Position.Latitude));

                this.Statistics.FieldTesterPositionAvailable = true;

                //create the H3 Geo Index according to the device reported position
                this.H3 = new H3Hashes();

                var h3_11_index = fieldTesterPoint.Coordinate.ToH3Index(11);

                this.H3.H3_11 = h3_11_index.ToString();                
                this.H3.H3_10 = h3_11_index.GetParentForResolution(10).ToString();
                this.H3.H3_9 = h3_11_index.GetParentForResolution(9).ToString();
                this.H3.H3_8 = h3_11_index.GetParentForResolution(8).ToString();
                this.H3.H3_7 = h3_11_index.GetParentForResolution(7).ToString();
            }    


            if (this.WirelessMetadata?.LoRaWAN?.Gateways?.Any() == true || this.WirelessMetadata?.LoRaWAN?.PublicGateways?.Any() == true)
            {
                this.Statistics.NumberOfPrivateGatewayReceivingUplink = this.WirelessMetadata?.LoRaWAN?.Gateways?.Count;
                this.Statistics.NumberOfPublicGatewayReceivingUplink = this.WirelessMetadata?.LoRaWAN?.PublicGateways?.Count;

                //get all gateways (private and public) and compute the statistics for each one of them
                List<GatewayBase> allGateways = new List<GatewayBase>();
                
                if ( this.WirelessMetadata?.LoRaWAN?.Gateways != null)
                    allGateways.AddRange( this.WirelessMetadata.LoRaWAN.Gateways);

                if ( this.WirelessMetadata?.LoRaWAN?.PublicGateways != null)
                    allGateways.AddRange( this.WirelessMetadata.LoRaWAN.PublicGateways);

                //get just gateways for which we know the position
                var gatewaysWithPosition = allGateways.Where(gateway => gateway.GatewayPosition != null);

                this.Statistics.GatewaysPositionAvailable = gatewaysWithPosition.Any();

                if (this.Statistics.GatewaysPositionAvailable && fieldTesterPoint != null)
                {
                    var distances = gatewaysWithPosition.Select(gateway =>
                    {
                        var gtwPoint = geometryFactory.CreatePoint(new Coordinate(
                            gateway.GatewayPosition.Longitude, gateway.GatewayPosition.Latitude));

                        //calculate an approximate distance in meters
                        return gtwPoint.Distance(fieldTesterPoint) * 111000.0;

                    }).ToList();

                    this.Statistics.MaxDistance = distances.Max();
                    this.Statistics.MinDistance = distances.Min();
                    this.Statistics.AvgDistance = distances.Average();
                }

                var rssiValues = allGateways.Select(g => g.Rssi).ToList();
                this.Statistics.MaxRSSI = rssiValues.Max();
                this.Statistics.MinRSSI = rssiValues.Min();
                this.Statistics.AvgRSSI = rssiValues.Average();

                var snrValues = allGateways.Select(g => g.Snr).ToList();
                this.Statistics.MaxSnr = snrValues.Max();
                this.Statistics.MinSnr = snrValues.Min();
                this.Statistics.AvgSnr = snrValues.Average();
            }  
        }


        private static int getWeekOfYear(DateTime date)
        {
            CultureInfo ciCurr = CultureInfo.CurrentCulture;
            int weekNum = ciCurr.Calendar.GetWeekOfYear(
                date, 
                ciCurr.DateTimeFormat.CalendarWeekRule, 
                ciCurr.DateTimeFormat.FirstDayOfWeek);
            return weekNum;
        }
    }

    public class H3Hashes
    {        
        public string H3_7 {get;set;}

        public string H3_8 {get;set;}

        public string H3_9 {get;set;}

        public string H3_10 {get;set;}

        public string H3_11 {get;set;}
    }

    
    public class FieldTestResultStatisticsBase
    {
        public bool FieldTesterPositionAvailable { get; set; }

        public double? MaxRSSI { get; set; }

        public double? AvgRSSI { get; set; }

        public double? MinRSSI { get; set; }

        public double? MaxSnr { get; set; }

        public double? AvgSnr { get; set; }

        public double? MinSnr { get; set; }
    }


    public class LoRaWANFieldTestResultStatistics : FieldTestResultStatisticsBase
    {
        public bool GatewaysPositionAvailable { get; set; }

        public double? MaxDistance { get; set; }

        public double? AvgDistance { get; set; }

        public double? MinDistance { get; set; }

        public int? NumberOfGatewayReceivingUplink { get
            {
                int numberOfGatewayReceivingUplink = 0;
                if (this.NumberOfPrivateGatewayReceivingUplink != null)
                    numberOfGatewayReceivingUplink += this.NumberOfPrivateGatewayReceivingUplink.Value;

                if (this.NumberOfPublicGatewayReceivingUplink != null)
                    numberOfGatewayReceivingUplink += this.NumberOfPublicGatewayReceivingUplink.Value;

                return numberOfGatewayReceivingUplink;
            }             
        }

        public int? NumberOfPrivateGatewayReceivingUplink { get; set; }

        public int? NumberOfPublicGatewayReceivingUplink { get; set; }
    }
}
