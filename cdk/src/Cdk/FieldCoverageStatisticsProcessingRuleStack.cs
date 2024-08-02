using Amazon.CDK;
using Constructs;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.IoT;
using Amazon.CDK.AWS.IAM;
using Cdklabs.CdkNag;
using System;

namespace Cdk
{
    public class FieldCoverageStatisticsProcessingRuleStack : Stack
    {
        internal FieldCoverageStatisticsProcessingRuleStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {        
            //"lorawan/uplink/senseCapT1000"
            var fieldSurveyResultTopicFilter = this.Node.TryGetContext("fieldSurveyResultTopicFilter") as string;
            Console.WriteLine("fieldSurveyResultTopicFilter -> " + fieldSurveyResultTopicFilter);

            var deliveryStreamName = this.Node.TryGetContext("deliveryStreamName") as string;
            Console.WriteLine("deliveryStreamName -> " + deliveryStreamName);

            
            //deploy the .NET Lambda that will schedule the downlink message in response of a proper uplink            
            var rakDownlinkLambda = getRAKDownlinkLambda(scope,id,props);

           
            // Create the IAM Role for the IoT Core Rule to being able to republish on a target Topic
            var fireHoseRuleActionRole = new Role(this, "FieldCoverageStatisticsProcessing_fireHoseRuleActionRole_RuleRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("iot.amazonaws.com"),
            });


            // add the policy to the role to put stuff in Kinesis            
            fireHoseRuleActionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { "firehose:PutRecord", "firehose:PutRecordBatch" },
                Resources = new[] { $"arn:aws:firehose:{ props.Env.Region }:{ props.Env.Account }:deliverystream/{deliveryStreamName}" },
            }));
      

            // Create the IoT Core Rule wich take the uplink message and execute the decoder function
            var iotRule = new CfnTopicRule(this, "FieldCoverageStatisticsProcessingRule", new CfnTopicRuleProps
            {
                RuleName = "FieldCoverageStatisticsProcessingRule",
                TopicRulePayload = new CfnTopicRule.TopicRulePayloadProperty
                {
                    AwsIotSqlVersion ="2016-03-23",
                                        
                    Sql = $"SELECT\n * \n FROM \"{ fieldSurveyResultTopicFilter }\" WHERE Statistics.FieldTesterPositionAvailable = true",

                    Actions = new[]
                    {                       
                        new CfnTopicRule.ActionProperty
                        {
                            Lambda = new CfnTopicRule.LambdaActionProperty()
                            {
                                FunctionArn = rakDownlinkLambda.FunctionArn
                            }
                        }  ,
                        new CfnTopicRule.ActionProperty
                        {
                            Firehose = new CfnTopicRule.FirehoseActionProperty()
                            {
                                DeliveryStreamName = deliveryStreamName,
                                RoleArn = fireHoseRuleActionRole.RoleArn,
                                Separator = "\n",
                                BatchMode = false
                            }
                        }                        
                    },
                },
            });
            
            //give the lambda the permission to be executed by the AWS IoT core rule
            rakDownlinkLambda.AddPermission("RAKDownlinkRuleExecPermission", new Permission {                
                SourceAccount = props.Env.Account,
                Principal = new ServicePrincipal("iot.amazonaws.com"),
                SourceArn = iotRule.AttrArn,                
                Action = "lambda:InvokeFunction"
            });    
           



            NagSuppressions.AddResourceSuppressions( scope , new[] 
                {
                    new Cdklabs.CdkNag.NagPackSuppression()
                    {
                        Id = "AwsSolutions-IAM5",
                        Reason = "Policy is detailed enought even if contains a wildcard (it includes teh lambda name and 'LogRetention' as resource)",                        
                    },
                    new Cdklabs.CdkNag.NagPackSuppression()
                    {
                        Id = "AwsSolutions-IAM4",
                        Reason = "Policy is detailed enought even if contains a wildcard (it includes teh lambda name and 'LogRetention' as resource)",                        
                    }
                }, true);   
        }




        private Function getRAKDownlinkLambda(Construct scope, string id, IStackProps props = null)
        {
            // Create IAM Role for the for the Lambda
            var executionLambdaRole = new Role(this, "ExecutionLambdaRole", new RoleProps
            {
                RoleName ="RAKDownlinLambdaExecutionRole",
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });

            // Add policies to roles
            executionLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { 
                                    "logs:CreateLogGroup",
                                    "logs:CreateLogStream",
                                    "logs:PutLogEvents" },
                Resources = new[] 
                {                         
                    $"arn:aws:logs:{ props.Env.Region }:{ props.Env.Account }:log-group:/aws/lambda/*" //{lambdaFunc.LogGroup.LogGroupName}"
                }                
            })); 

            //allow the lambda to publish a message to AWS IoT Core             
            //the MQTT Topic used in the Lambda is the following: "lorawan/fieldTesterResult/{uplink.FieldTesterModel}/{uplink.WirelessMetadata.LoRaWAN.DevEui}"

            //it would be better to reduce the actions and resources in this policy

            executionLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps {
                Effect = Effect.ALLOW,
                Actions = new[] { "iot:*" },
                Resources = new[] { "*" }
            }));

            //allow the lambda to call methods from AWS wireless

            //it would be better to reduce the actions and resources in this policy
            
            executionLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps {
                Effect = Effect.ALLOW,
                Actions = new[] { "iotwireless:*" },
                Resources = new[] { "*" }
            }));


            var lambdaRotationPolicyRole = new Role(this, "LambdaRotationPolicyRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            });
            
            // Add policies to roles
            lambdaRotationPolicyRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { 
                                    "logs:CreateLogGroup",
                                    "logs:CreateLogStream",
                                    "logs:PutLogEvents" },
                Resources = new[] 
                    {                         
                        $"arn:aws:logs:{ props.Env.Region }:{ props.Env.Account }:log-group:/aws/lambda/*"
                    }                
            })); 
                        

             //define the build option for the lambda (this will be built together with the CDK deployment)
            var buildOption = new BundlingOptions()
            {
                Image = Runtime.DOTNET_8.BundlingImage,
                User = "root",
                OutputType = BundlingOutput.ARCHIVED,
                Command = new string[]{
               "/bin/sh",
                "-c",
                " dotnet tool install -g Amazon.Lambda.Tools"+
                " && cd field-coverage-downlink-rak/src/field-coverage-downlink-rak" +
                " && dotnet build"+
                " && dotnet lambda package --output-package /asset-output/function.zip"
                }                
            };

            // Lambda properties
            var lambdaFuncProperties = new FunctionProps
            {
                Runtime = Runtime.DOTNET_8,
                MemorySize = 512,
                Timeout =  Duration.Seconds(5.0),            
                Handler = "field-coverage-downlink-rak::field_coverage_downlink_rak.Function::FunctionHandler",                
                Code = Code.FromAsset("../", new Amazon.CDK.AWS.S3.Assets.AssetOptions
                {
                    Bundling = buildOption
                }),
                Role = executionLambdaRole,
                LogRetention = RetentionDays.ONE_DAY,
                LogRetentionRole = lambdaRotationPolicyRole
            };


            // Lambda
            return new Function(this, "RAKDownlinkLambda", lambdaFuncProperties); 
        }
    }
}
