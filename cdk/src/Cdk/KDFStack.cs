using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.KinesisFirehose;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace Cdk
{
    public class KDFStack : Stack
    {
        internal KDFStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            var fieldCoverageDataFirehoseBucketName = this.Node.TryGetContext("fieldCoverageDataFirehoseBucketName") as string;
            Console.WriteLine("fieldCoverageDataFirehoseBucketName -> " + fieldCoverageDataFirehoseBucketName);

            var deliveryStreamName = this.Node.TryGetContext("deliveryStreamName") as string;
            Console.WriteLine("deliveryStreamName -> " + deliveryStreamName);

            // Define the logging bucket for access logs
            var loggingBucket = new Bucket(this, "FirehoseLoggingBucket", new BucketProps
            {
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteObjects = true
            });

            // Enforce SSL connections for the logging bucket
            loggingBucket.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.DENY,
                Principals = new[] { new AnyPrincipal() },
                Actions = new[] { "s3:*" },
                Resources = new[] { loggingBucket.BucketArn, loggingBucket.ArnForObjects("*") },
                Conditions = new Dictionary<string, object>
                {
                    { "Bool", new Dictionary<string, object> { { "aws:SecureTransport", false } } }
                }
            }));

            // Define the S3 bucket (create if not existing)
            var firehoseBucket = new Bucket(this, "FieldCoverageDataFirehoseBucket", new BucketProps
            {
                BucketName = fieldCoverageDataFirehoseBucketName,
                RemovalPolicy = RemovalPolicy.DESTROY, // Adjust based on your needs (e.g., keep if data needs to be retained)
                AutoDeleteObjects = true, // Ensure this is set to true only if you want to delete objects on bucket removal
                ServerAccessLogsBucket = loggingBucket,
                ServerAccessLogsPrefix = "access-logs/"
            });

            // Enforce SSL connections for the main bucket
            firehoseBucket.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.DENY,
                Principals = new[] { new AnyPrincipal() },
                Actions = new[] { "s3:*" },
                Resources = new[] { firehoseBucket.BucketArn, firehoseBucket.ArnForObjects("*") },
                Conditions = new Dictionary<string, object>
                {
                    { "Bool", new Dictionary<string, object> { { "aws:SecureTransport", false } } }
                }
            }));

            // Define the IAM role for Firehose  
            // it would be better to narrow down the permissions
            var firehoseRole = new Role(this, "FirehoseRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("firehose.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("AmazonS3FullAccess"),
                    ManagedPolicy.FromAwsManagedPolicyName("AmazonKinesisFirehoseFullAccess")
                }
            });

            // Define the Firehose stream
            var firehoseStream = new CfnDeliveryStream(this, deliveryStreamName + "_delivery_stream", new CfnDeliveryStreamProps
            {
                DeliveryStreamName = deliveryStreamName,
                DeliveryStreamType = "DirectPut",
                ExtendedS3DestinationConfiguration = new CfnDeliveryStream.ExtendedS3DestinationConfigurationProperty
                {
                    RoleArn = firehoseRole.RoleArn,
                    BucketArn = firehoseBucket.BucketArn,
                    //prefix the hive partition to avoid duplicated properties in the table when use a glue crawdler to scan the datalake and generate automatically the table and the partitions
                    Prefix = "prt_quality=good/prt_year=!{partitionKeyFromQuery:year}/prt_week=!{partitionKeyFromQuery:week}/prt_technology=!{partitionKeyFromQuery:technology}/prt_operator=!{partitionKeyFromQuery:operator}/prt_deviceType=!{partitionKeyFromQuery:deviceType}/prt_deviceId=!{partitionKeyFromQuery:deviceId}/prt_h7=!{partitionKeyFromQuery:h7}/prt_h8=!{partitionKeyFromQuery:h8}/prt_h9=!{partitionKeyFromQuery:h9}/prt_h10=!{partitionKeyFromQuery:h10}/prt_h11=!{partitionKeyFromQuery:h11}/",
                    ErrorOutputPrefix = "prt_quality=bad/",
                    BufferingHints = new CfnDeliveryStream.BufferingHintsProperty
                    {
                        SizeInMBs = 64,
                        IntervalInSeconds = 60
                    },
                    CompressionFormat = "UNCOMPRESSED",
                    EncryptionConfiguration = new CfnDeliveryStream.EncryptionConfigurationProperty
                    {
                        NoEncryptionConfig = "NoEncryption"
                    },                    
                    CloudWatchLoggingOptions = new CfnDeliveryStream.CloudWatchLoggingOptionsProperty
                    {
                        Enabled = true,
                        LogGroupName = "/aws/kinesisfirehose/" +deliveryStreamName + "_delivery_stream",
                        LogStreamName = "DestinationDelivery"
                    },                    
                    ProcessingConfiguration = new CfnDeliveryStream.ProcessingConfigurationProperty
                    {
                        Enabled = true,
                        Processors = new[]
                        {
                            new CfnDeliveryStream.ProcessorProperty
                            {
                                Type = "RecordDeAggregation",
                                Parameters = new[]
                                {
                                    new CfnDeliveryStream.ProcessorParameterProperty
                                    {
                                        ParameterName = "SubRecordType",
                                        ParameterValue = "JSON"
                                    }
                                }
                            },
                            new CfnDeliveryStream.ProcessorProperty
                            {
                                Type = "MetadataExtraction",
                                Parameters = new[]
                                {
                                    new CfnDeliveryStream.ProcessorParameterProperty
                                    {
                                        ParameterName = "MetadataExtractionQuery",
                                        ParameterValue = "{year:.Year,week:.Week,technology:.Technology,operator:.Operator,deviceType:.WirelessDeviceType,deviceId:.WirelessDeviceId,h7:.H3.H3_7,h8:.H3.H3_8,h9:.H3.H3_9,h10:.H3.H3_10,h11:.H3.H3_11}"
                                    },
                                    new CfnDeliveryStream.ProcessorParameterProperty
                                    {
                                        ParameterName = "JsonParsingEngine",
                                        ParameterValue = "JQ-1.6"
                                    }
                                }
                            },
                            new CfnDeliveryStream.ProcessorProperty
                            {
                                Type = "AppendDelimiterToRecord",
                                Parameters = new CfnDeliveryStream.ProcessorParameterProperty[]{ }
                            }
                        }
                    },
                    S3BackupMode = "Disabled",
                    DynamicPartitioningConfiguration = new CfnDeliveryStream.DynamicPartitioningConfigurationProperty
                    {
                        Enabled = true,
                        RetryOptions = new CfnDeliveryStream.RetryOptionsProperty
                        {
                            DurationInSeconds = 300
                        }
                    }
                },
                DeliveryStreamEncryptionConfigurationInput = new CfnDeliveryStream.DeliveryStreamEncryptionConfigurationInputProperty
                {
                    KeyType = "AWS_OWNED_CMK"
                }
            });

            // Grant the Firehose role permissions to use the S3 bucket
            firehoseBucket.GrantReadWrite(firehoseRole);

            // Output the S3 bucket name
            new CfnOutput(this, "FirehoseBucketArn", new CfnOutputProps
            {
                Value = firehoseBucket.BucketArn,
                ExportName = "FirehoseBucketArn"
            });

            // Output the Firehose stream name
            new CfnOutput(this, "FirehoseStreamArn", new CfnOutputProps
            {
                Value = firehoseStream.AttrArn,
                ExportName = "FirehoseStreamArn"
            });
        }
    }
}
