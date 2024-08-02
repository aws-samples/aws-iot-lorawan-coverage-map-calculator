using Amazon.Lambda.Core;
using Amazon.XRay.Recorder.Handlers.AwsSdk;

using Amazon.IoTWireless;
using Amazon.IoTWireless.Model;

using Newtonsoft.Json;

using NetTopologySuite.IO;
using NetTopologySuite.Geometries;

using System.Linq;
using Amazon.IotData.Model;
using Amazon;
using Amazon.IoT;
using Amazon.IoT.Model;
using field_coverage_lambda;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(field_coverage_lambda.LambdaEnumSerializer))] //Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer

namespace field_coverage_downlink_rak;

public class Function
{
    static AmazonIoTWirelessClient _amazonIoTWirelessClient;

    //static AmazonIotDataClient _amazonIoTDataClient;

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
    public async Task FunctionHandler(FieldTestResult fieldTestResult, ILambdaContext context)
    {
        await initializeIoTClient();

        LambdaLogger.Log($"Got this FieldSurveyResult: {JsonConvert.SerializeObject(fieldTestResult, Formatting.Indented)}");

        if ( fieldTestResult.WirelessDeviceType == "RakTester")
        {
            LambdaLogger.Log($"Sending Downlink message to {fieldTestResult.WirelessDeviceId}");

            //just send a downlink message to the Field Tester device from RAK
            //https://github.com/disk91/WioLoRaWANFieldTester/blob/master/doc/DEVELOPMENT.md#frame-format

            //downlink response format on port 2:

            var downlinkResponse = createDownlinkPayload(fieldTestResult.WirelessMetadata.LoRaWAN.FCnt,
                fieldTestResult.Statistics.MinRSSI ?? 0, fieldTestResult.Statistics.MaxRSSI ?? 0,
                fieldTestResult.Statistics.MinDistance ?? 0, fieldTestResult.Statistics.MaxDistance ?? 0,
                fieldTestResult.Statistics.NumberOfGatewayReceivingUplink ?? 0
            );

            LambdaLogger.Log($"DownlinkPayload BASE64: {Convert.ToBase64String(downlinkResponse)}");

            var sendDownlinkResponse = await sendDownlinkMessage(fieldTestResult.WirelessDeviceId, downlinkResponse, 2);

            LambdaLogger.Log($"Got this response from Send Downlink Message ops: {JsonConvert.SerializeObject(sendDownlinkResponse, Formatting.Indented)}");
        }
        else
        {
            LambdaLogger.Log("This is not a RAK Wireless field tester!");
        }
    }

    private byte[] createDownlinkPayload(int sequenceId, double minRssi, double maxRssi, double minDistance, double maxDistance, int seenHotspot)
    {
        //Byte	Usage
        //0	Sequence ID % 255
        //1	Min Rssi + 200 (160 = -40dBm)
        //2	Max Rssi + 200 (160 = -40dBm)
        //3	Min Distance step 250m
        //4	Max Distance step 250m
        //5	Seen hotspot

        minDistance = 10;

        byte[] payload = new byte[6];
        payload[0] = (byte)(sequenceId % 255);
        payload[1] = (byte)(minRssi + 200.0);
        payload[2] = (byte)(maxRssi + 200.0);
        payload[3] = (byte)(Math.Round(minDistance) / 250.0);
        payload[4] = (byte)(Math.Round(maxDistance) / 250.0);
        payload[5] = (byte)seenHotspot;
        return payload;
    }

    private async Task<SendDataToWirelessDeviceResponse> sendDownlinkMessage(string wirelessDeviceId, byte[] downlinkPayload, int fPort, int ack = 0)
    {
        var sendRequest = new SendDataToWirelessDeviceRequest
        {
            Id = wirelessDeviceId,
            PayloadData = Convert.ToBase64String(downlinkPayload),
            TransmitMode = ack, //un aknowledge by default
            WirelessMetadata = new Amazon.IoTWireless.Model.WirelessMetadata
            {
                LoRaWAN = new LoRaWANSendDataToDevice
                {
                    FPort = fPort
                }
            }
        };

        var response = await _amazonIoTWirelessClient.SendDataToWirelessDeviceAsync(sendRequest);
        
        return response;
    }

    private async Task initializeIoTClient()
    {
        if ( _amazonIoTWirelessClient == null)
            _amazonIoTWirelessClient  = new AmazonIoTWirelessClient();

        /*
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
        */          
    }
}
