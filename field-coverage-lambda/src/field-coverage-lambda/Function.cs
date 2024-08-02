using Amazon.Lambda.Core;
using Amazon.XRay.Recorder.Handlers.AwsSdk;

using Amazon.IoTWireless;
using Amazon.IoTWireless.Model;

using Amazon.IotData;

using Newtonsoft.Json;

using NetTopologySuite.IO;
using NetTopologySuite.Geometries;

using System.Linq;
using Amazon.IotData.Model;
using Amazon;
using Amazon.IoT;
using Amazon.IoT.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(field_coverage_lambda.LambdaEnumSerializer))] //Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer

namespace field_coverage_lambda
{
    public class Function
    {   
        //ensure this value is aligned to the CDK fieldSurveyResultTopicFilter used to generate
        //the rule engine rule that is triggered by the MQTT message emitted by this lambda
        private static string fieldSurveyResultTopicBase = "lorawan/fieldSurveyResult";


        static AmazonIoTWirelessClient _amazonIoTWirelessClient;
        static AmazonIotDataClient _amazonIoTDataClient;

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            AWSSDKHandler.RegisterXRayForAllServices();
        }


        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(FieldTesterUplink uplink, ILambdaContext context)
        {
            await initializeIoTClient();

            LambdaLogger.Log($"Got this FieldTesterUplink: {JsonConvert.SerializeObject(uplink, Formatting.Indented)}");

            if ( !uplink.DecodedUplink.IsValid )
            {
                //payload wasn't able to be decoded properly just skip!
                return;
            }

            NetTopologySuite.Geometries.Point fieldTesterPosition = null;

            if ( uplink.DecodedUplink.PositionType == PositionTypeEnum.GPS || uplink.DecodedUplink.PositionType == PositionTypeEnum.Indoor)
            {
                LambdaLogger.Log("Field tester send a valid position!");

                fieldTesterPosition = new NetTopologySuite.Geometries.Point(
                    uplink.DecodedUplink.Position.Longitude,
                    uplink.DecodedUplink.Position.Latitude );
            }
            else
            {
                //ok the uplink doesn't contain a valid information! just discard it!
                LambdaLogger.Log("Field tester didn't send a position! Try to get from Manual position stored into serivce if available.");

                fieldTesterPosition = await retreivePositionFromAICL(uplink);
            }

            await processUplinkAndPosition(uplink, fieldTesterPosition);
        }

      
        //try to retreive the position from AICL for device that is probably static and that store the position as configuration in AICL
        private async Task<Point?> retreivePositionFromAICL(FieldTesterUplink uplink)
        {
            //TODO implement the search of the device position in AICL

            uplink.DecodedUplink.PositionType = PositionTypeEnum.Unknown;  

            return null;
        }
       
        private async Task processUplinkAndPosition(FieldTesterUplink uplink, NetTopologySuite.Geometries.Point fieldTesterPosition)
        {
            //load Gateways positions (for those availables)
            await loadGatewayPositions(uplink);
            
            FieldTestResult fieldTestResult = new FieldTestResult(uplink);

            await loadWirelessDeviceDetails(fieldTestResult);

            //compute the statistics (max min distances, RSSI and SNR according to data availability for Gateways)
            fieldTestResult.ComputeStatistics();

            var fieldTesterResultJson = JsonConvert.SerializeObject(fieldTestResult, Formatting.Indented);

            //write into logs! 
            LambdaLogger.Log(fieldTesterResultJson);

            //now republish this message to IoT Core! 
            //see note on top as this topic will be the trigger for a rule engine rule!!
        
            string mqttTopic = $"{fieldSurveyResultTopicBase}/{uplink.WirelessDeviceType}/{uplink.WirelessMetadata.LoRaWAN.DevEui}";

            PublishRequest publishRequest = new  PublishRequest()
            {
                Topic = mqttTopic,
                ContentType ="application/json",
                Qos = 0,                      
                Payload = new MemoryStream( System.Text.Encoding.UTF8.GetBytes(fieldTesterResultJson) )
            };

            LambdaLogger.Log($"Republishing to this topic: {mqttTopic}");

            await _amazonIoTDataClient.PublishAsync(publishRequest);
        }

        private async Task loadWirelessDeviceDetails(FieldTestResult fieldTestResult)
        {
            LambdaLogger.Log($"Loading Device Name by Device Id: {fieldTestResult.WirelessDeviceId}");

            var resourceRequest = new GetWirelessDeviceRequest
            {
                Identifier = fieldTestResult.WirelessDeviceId,
                IdentifierType = WirelessDeviceIdType.WirelessDeviceId           
            };
                       
            try
            {
                var deviceResponse = await _amazonIoTWirelessClient.GetWirelessDeviceAsync(resourceRequest);

                LambdaLogger.Log(JsonConvert.SerializeObject(deviceResponse, Formatting.Indented));

                fieldTestResult.DeviceName = deviceResponse.Name;
            }
            catch (Exception e)
            {
                LambdaLogger.Log(e.Message);
            }
        }

        private async Task loadGatewayPositions(FieldTesterUplink uplink)
        {
            if ( uplink!= null && uplink.WirelessMetadata != null && uplink.WirelessMetadata.LoRaWAN != null && uplink.WirelessMetadata.LoRaWAN.Gateways != null)
            {
                LambdaLogger.Log($"Loading positions for {uplink.WirelessMetadata.LoRaWAN.Gateways.Count} Private Gateway");

                var tasks = uplink.WirelessMetadata.LoRaWAN.Gateways.Select(async gateway =>
                {
                    var gatewayPositionGeo = await getGatewayPosition(gateway.GatewayEui);

                    if ( gatewayPositionGeo != null)
                    {
                        gateway.GatewayPosition = new Position(){ 
                            Latitude = gatewayPositionGeo.Coordinate.Y, 
                            Longitude = gatewayPositionGeo.Coordinate.X };
                    }
                    else
                    {
                        //TODO: fallback strategy for unknown gateways position??
                
                        LambdaLogger.Log($"Can't obtain position for gateway: {gateway.GatewayEui}");
                    }
                });

                await Task.WhenAll(tasks);
            }   

            //for public gateways right now there is no way to obtain their position                 
        }

        private async Task initializeIoTClient()
        {
            if ( _amazonIoTWirelessClient == null)
                _amazonIoTWirelessClient  = new AmazonIoTWirelessClient();

            if (_amazonIoTDataClient == null)
            {
                var region = RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("AWS_REGION"));

                var iotClient = new AmazonIoTClient(region);

                // Recupera l'endpoint di AWS IoT Core
                var endpointResponse = await iotClient.DescribeEndpointAsync(new DescribeEndpointRequest
                {
                    EndpointType = "iot:Data-ATS"
                });

                var iotEndpoint = endpointResponse.EndpointAddress;

                // Configura AmazonIotDataClient con l'endpoint e la regione recuperati
                var iotDataConfig = new AmazonIotDataConfig
                {
                    RegionEndpoint = region,
                    ServiceURL = $"https://{iotEndpoint}"
                };
            
                _amazonIoTDataClient = new AmazonIotDataClient(iotDataConfig);                
            }            
        }

        private async Task<Geometry> getGatewayPosition(string gatewayEui)
        {
            var resourceRequest = new GetWirelessGatewayRequest
            {
                Identifier = gatewayEui,
                IdentifierType = WirelessGatewayIdType.GatewayEui,           
            };
                       
            try
            {
                var gatewayResponse = await _amazonIoTWirelessClient.GetWirelessGatewayAsync(resourceRequest);

                Console.Write(JsonConvert.SerializeObject(gatewayResponse, Formatting.Indented));

                var positionRequest = new GetResourcePositionRequest
                {
                    ResourceIdentifier = gatewayResponse.Id,
                    ResourceType = PositionResourceType.WirelessGateway
                };
              
                var positionResponse = await _amazonIoTWirelessClient.GetResourcePositionAsync(positionRequest);

                // convert stream to string
                StreamReader reader = new StreamReader(positionResponse.GeoJsonPayload);
                string geoJson = reader.ReadToEnd();
                                
                Geometry gatewayPosition;
                
                var serializer = GeoJsonSerializer.Create();
                using (var stringReader = new StringReader(geoJson))
                using (var jsonReader = new JsonTextReader(stringReader))
                {
                    gatewayPosition = serializer.Deserialize<Geometry>(jsonReader);
                }
                
                LambdaLogger.Log($"Gateway Position --> Long: {gatewayPosition.Coordinate.X}, Lat: {gatewayPosition.Coordinate.Y}");

                return gatewayPosition;
            }
            catch (Exception e)
            {
                // Gestisci eventuali eccezioni qui
                LambdaLogger.Log(e.Message);
                return null;
            }
        }
    }
}

