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
    public class FieldCoverageStatisticsLambdaStack: Stack
    {  
        public string FieldCoverageStatisticsLambdaArn {get;set;}

        internal FieldCoverageStatisticsLambdaStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // Create IAM Role for the for the Lambda
            var executionLambdaRole = new Role(this, "ExecutionLambdaRole", new RoleProps
            {
                RoleName ="FieldCoverageStatisticsLambdaExecutionRole",
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
                " && cd field-coverage-lambda/src/field-coverage-lambda" +
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
                Handler = "field-coverage-lambda::field_coverage_lambda.Function::FunctionHandler",                
                Code = Code.FromAsset("../", new Amazon.CDK.AWS.S3.Assets.AssetOptions
                {
                    Bundling = buildOption
                }),
                Role = executionLambdaRole,
                LogRetention = RetentionDays.ONE_DAY,
                LogRetentionRole = lambdaRotationPolicyRole
            };


            // Lambda
            var lambda = new Function(this, "FieldCoverageStatisticsLambda", lambdaFuncProperties); 
 
            this.FieldCoverageStatisticsLambdaArn = lambda.FunctionArn;
                          
            //give the lambda the permission to be executed by the AWS IoT core rule
            lambda.AddPermission("FieldCoverageStatisticsLambdaExecPermissionSeedStudioDecoder", new Permission {                
                SourceAccount = props.Env.Account,
                Principal = new ServicePrincipal("iot.amazonaws.com"),
                SourceArn = $"arn:aws:iot:{ props.Env.Region }:{ props.Env.Account }:rule/SeedStudioT1000PayloadDecoderRule",                
                Action = "lambda:InvokeFunction"
            });
                     
            //give the lambda the permission to be executed by the AWS IoT core rule
            lambda.AddPermission("FieldCoverageStatisticsLambdaExecPermissionRAKWirelessDecoder", new Permission { 
                SourceAccount = props.Env.Account,
                Principal = new ServicePrincipal("iot.amazonaws.com"),
                SourceArn = $"arn:aws:iot:{ props.Env.Region }:{ props.Env.Account }:rule/RAKWirelessRAK10701PayloadDecoderRule",                
                Action = "lambda:InvokeFunction"
            });                       
        }        
    }
}
