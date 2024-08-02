using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;
using Cdklabs.CdkNag;

namespace Cdk
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();

            //Add cdk-nag
            Aspects.Of(app)
                .Add(
                        new AwsSolutionsChecks( 
                            new Cdklabs.CdkNag.NagPackProps()
                            { 
                                Verbose = true                                
                            })
                    );

            var fieldCoverageStatisticsLambdaStack = new FieldCoverageStatisticsLambdaStack(app, "FieldCoverageStatisticsLambda", new StackProps
            {   
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }                
            });
          
            var seedStudioT1000PayloadDecoderStack = new SeedStudioT1000PayloadDecoderStack(app, "SeedStudioT1000PayloadDecoder", new StackProps
            {               
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }                
            },fieldCoverageStatisticsLambdaStack.FieldCoverageStatisticsLambdaArn);

            // Set dependency
            seedStudioT1000PayloadDecoderStack.AddDependency(fieldCoverageStatisticsLambdaStack);


            var rakWirelessRAK10701PayloadDecderStack = new RAKWirelessRAK10701PayloadDecoderStack(app, "RAKWirelessRAK10701PayloadDecoder", new StackProps
            {   
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }                             
            },fieldCoverageStatisticsLambdaStack.FieldCoverageStatisticsLambdaArn);

            // Set dependency
            rakWirelessRAK10701PayloadDecderStack.AddDependency(fieldCoverageStatisticsLambdaStack);     


            
            var kdfStack = new KDFStack(app, "KDFStack", new StackProps
            {                
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }                            
            });

            var fieldCoverageStatisticsProcessingRuleStack = new FieldCoverageStatisticsProcessingRuleStack(app, "FieldCoverageStatisticsProcessingRule", new StackProps
            {
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }                            
            });

            fieldCoverageStatisticsProcessingRuleStack.AddDependency(kdfStack);

            var glueAthenaStack = new GlueAthenaStack(app, "GlueAthenaStack", new StackProps
            {                
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }                
            });

            glueAthenaStack.AddDependency(kdfStack);


            var loRaWanStack = new LoRaWanStack(app, "LoRaWanStack", new StackProps
            {
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }
            });

           

            app.Synth();
        }
    }
}
