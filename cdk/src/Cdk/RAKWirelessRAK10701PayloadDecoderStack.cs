using Amazon.CDK;
using Constructs;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.IoT;
using Amazon.CDK.AWS.IAM;
using Cdklabs.CdkNag;
using System;
using Amazon.CDK.AWS.IoTWireless;

namespace Cdk
{
    public class RAKWirelessRAK10701PayloadDecoderStack : Stack
    {
        internal RAKWirelessRAK10701PayloadDecoderStack(Construct scope, string id, IStackProps props, string fieldCoverageStatisticsLambdaArn) : base(scope, id, props)
        {
            var uplinkTopicFilter = this.Node.TryGetContext("uplinkTopicFilterRAK10701") as string;
            Console.WriteLine("uplinkTopicFilterRAK10701 -> " + uplinkTopicFilter);

            var decodedDataTopic = this.Node.TryGetContext("decodedDataTopicRAK10701") as string;
            Console.WriteLine("decodedDataTopicRAK10701 -> " + decodedDataTopic);

                 
            //LoRaWAN destination - Define IAM role for Destination
            var destinationRole = new Role(this, "DestinationRole_RAKWirelessRAK1070", new RoleProps
            {                 
                AssumedBy = new ServicePrincipal("iotwireless.amazonaws.com")
            });

            // Define the IAM policy for the Destination Role
            var destinationPolicy = new Policy(this, "DestinationPolicy_RAKWirelessRAK1070", new PolicyProps
            {
                Statements = new[]
                {
                    new PolicyStatement(new PolicyStatementProps
                    {
                        Effect = Effect.ALLOW,
                        Actions = new[] { "iot:Publish" },
                        Resources = new[] { $"arn:aws:iot:{ props.Env.Region }:{ props.Env.Account }:topic/{uplinkTopicFilter}"  }
                    }),
                    new PolicyStatement(new PolicyStatementProps
                    {
                        Effect = Effect.ALLOW,
                        Actions = new[] { "iot:DescribeEndpoint" },
                        Resources = new[] { "*" }
                    })
                }
            });

            destinationPolicy.AttachToRole(destinationRole);

            // Define LoRaWAN Destination
            new Amazon.CDK.AWS.IoTWireless.CfnDestination(this, "RAKWirelessDestination", new Amazon.CDK.AWS.IoTWireless.CfnDestinationProps
            { 
                Name = "RAKWireless-rak10701",                 
                Expression = uplinkTopicFilter,
                ExpressionType = "MqttTopic",
                RoleArn = destinationRole.RoleArn
            });



        
            // Create IAM Role for the for the Lambda
            var lambdaRole = new Role(this, "RAKWirelessRAK10701PayloadDecoderLambdaRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            });
               
            var lambdaRotationPolicyRole = new Role(this, "LambdaRotationPolicyRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            });


            // Lambda properties
            var lambdaFuncProperties = new FunctionProps
            {
                Runtime = Runtime.NODEJS_20_X,
                MemorySize = 128,               
                Handler = "index.handler",
                Code = Code.FromAsset("../lorawan-decoder-rak-wireless-rak10701/src")
                //Role = lambdaRole
            };

            // Define the Lambda function
            var decoderLambdaFunction = new Function(this, "RAKWirelessRAK10701PayloadDecoderLambda", lambdaFuncProperties);
                        
            // Add policies to roles
            lambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { 
                                    "logs:CreateLogGroup",
                                    "logs:CreateLogStream",
                                    "logs:PutLogEvents" },
                Resources = new[] 
                    {                         
                        $"arn:aws:logs:{ props.Env.Region }:{ props.Env.Account }:log-group:/aws/lambda/{decoderLambdaFunction.LogGroup.LogGroupName}"
                    }                
            })); 
         
            lambdaFuncProperties.Role = lambdaRole;

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
                        $"arn:aws:logs:{ props.Env.Region }:{ props.Env.Account }:log-group:/aws/lambda/{decoderLambdaFunction.FunctionName}-LogRetention*"
                    }                
            })); 
          
            lambdaFuncProperties.LogRetention = RetentionDays.ONE_DAY;
            lambdaFuncProperties.LogRetentionRole = lambdaRotationPolicyRole;
           
           


           
            // Create the IAM Role for the IoT Core Rule to being able to republish on a target Topic
            var ruleRole = new Role(this, "RAKWirelessRAK10701PayloadDecoderRuleRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("iot.amazonaws.com"),
            });


            // add the policy to the role
            ruleRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { "iot:Publish" },
                Resources = new[] { $"arn:aws:iot:{ props.Env.Region }:{ props.Env.Account }:topic/*" },
            }));
                       

            // Create the IoT Core Rule wich take the uplink message and execute the decoder function
            var iotRule = new CfnTopicRule(this, "RAKWirelessRAK10701PayloadDecoderRule", new CfnTopicRuleProps
            {
                RuleName = "RAKWirelessRAK10701PayloadDecoderRule",
                TopicRulePayload = new CfnTopicRule.TopicRulePayloadProperty
                {
                    AwsIotSqlVersion ="2016-03-23",
                    
                    Sql = $"SELECT\n *, \"RakTester\" as WirelessDeviceType, aws_lambda(\"{ decoderLambdaFunction.FunctionArn }\", {{\"PayloadData\":PayloadData, \"WirelessMetadata\": WirelessMetadata }}) as DecodedUplink\nFROM \"{uplinkTopicFilter}\"",

                    Actions = new[]
                    {   
                        new CfnTopicRule.ActionProperty
                        {
                            Republish = new CfnTopicRule.RepublishActionProperty
                            {
                                Topic = decodedDataTopic,
                                Qos = 1,
                                RoleArn = ruleRole.RoleArn                            
                            }
                        },                        
                        new CfnTopicRule.ActionProperty
                        {
                            Lambda = new CfnTopicRule.LambdaActionProperty()
                            {
                                FunctionArn = fieldCoverageStatisticsLambdaArn
                            }
                        }                        
                    },
                },
            });
            
            //give the lambda the permission to be executed by the AWS IoT core rule
            decoderLambdaFunction.AddPermission("RAKWirelessRAK10701PayloadDecoderRuleExecPermission", new Permission {                
                SourceAccount = props.Env.Account,
                Principal = new ServicePrincipal("iot.amazonaws.com"),
                SourceArn = iotRule.AttrArn,                
                Action = "lambda:InvokeFunction"
            });    
         

            //The permission to execute the lambda from the rule will be addded direcly when creating the Lambda ange leveraging naming convention           


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
    }
}
