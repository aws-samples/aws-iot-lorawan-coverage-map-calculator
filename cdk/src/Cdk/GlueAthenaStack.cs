using Amazon.CDK;
using Amazon.CDK.AWS.Glue;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Constructs;
using Amazon.CDK.AWS.Athena;
using System;
using System.Collections.Generic;
using Cdklabs.CdkNag;

namespace Cdk
{
    public class GlueAthenaStack : Stack
    {
        internal GlueAthenaStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            var athenaDatabaseName = this.Node.TryGetContext("athenaDatabaseName") as string;
            Console.WriteLine("athenaDatabaseName -> " + athenaDatabaseName);

            var athenaTableName = this.Node.TryGetContext("athenaTableName") as string;
            Console.WriteLine("athenaTableName -> " + athenaTableName);

            // Import the Lambda function ARN from the first stack
            var firehoseBucketArn = Fn.ImportValue("FirehoseBucketArn");
            Console.WriteLine("Imported FirehoseBucketArn -> " + firehoseBucketArn);

            var athenaQueryResultBuckerName = this.Node.TryGetContext("athenaQueryResultBuckerName") as string;
            Console.WriteLine("athenaQueryResultBuckerName -> " + athenaQueryResultBuckerName);

            // Define the S3 bucket (assuming it already exists)
            var s3Bucket = Bucket.FromBucketArn(this, "FirehoseBucket", firehoseBucketArn);

            // Define IAM Role for Glue Crawler
            var glueRole = new Role(this, "GlueCrawlerRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("glue.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSGlueServiceRole")
                }
            });

            s3Bucket.GrantRead(glueRole);
            

             // Define Glue Database
            var glueDatabase = new CfnDatabase(this, "FieldCoverageDataDatabase", new CfnDatabaseProps
            {
                CatalogId = this.Account,
                DatabaseInput = new CfnDatabase.DatabaseInputProperty
                {
                    Name = athenaDatabaseName
                }
            });


            // Define Glue Crawler
            var glueCrawler = new CfnCrawler(this, "FieldCoverageDataCrawler", new CfnCrawlerProps
            {
                Role = glueRole.RoleArn,
                DatabaseName = athenaDatabaseName,
                Name = athenaTableName + "_crawler",
                Targets = new CfnCrawler.TargetsProperty
                {
                    S3Targets = new[]
                    {
                        new CfnCrawler.S3TargetProperty
                        {
                            Path = "s3://" +s3Bucket.BucketName + "/"
                        }
                    }
                },
                SchemaChangePolicy = new CfnCrawler.SchemaChangePolicyProperty
                {
                    UpdateBehavior = "UPDATE_IN_DATABASE",
                    DeleteBehavior = "DEPRECATE_IN_DATABASE"
                },
                RecrawlPolicy = new CfnCrawler.RecrawlPolicyProperty
                {
                    RecrawlBehavior = "CRAWL_EVERYTHING"
                },
                Configuration = "{\"Version\":1.0,\"CreatePartitionIndex\":true}"            
            });     


            // Define the S3 bucket for the query result (create if not existing)
            var athenaQueryResultBucker = new Bucket(this, "AthenaQueryResultBucker", new BucketProps
            {
                BucketName = athenaQueryResultBuckerName,
                Versioned = false,
                RemovalPolicy = RemovalPolicy.DESTROY, // Adjust based on your needs (e.g., keep if data needs to be retained)
                AutoDeleteObjects = true, // Ensure this is set to true only if you want to delete objects on bucket removal                
            });

            // Enforce SSL connections for the main bucket
            athenaQueryResultBucker.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.DENY,
                Principals = new[] { new AnyPrincipal() },
                Actions = new[] { "s3:*" },
                Resources = new[] { athenaQueryResultBucker.BucketArn, athenaQueryResultBucker.ArnForObjects("*") },
                Conditions = new Dictionary<string, object>
                {
                    { "Bool", new Dictionary<string, object> { { "aws:SecureTransport", false } } }
                }
            }));






            
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
                    },
                    new Cdklabs.CdkNag.NagPackSuppression()
                    {
                        Id = "AwsSolutions-GL1",
                        Reason = "The Glue crawler or job does not use a security configuration with CloudWatch Log encryption enabled. Enabling encryption at rest helps prevent unauthorized users from getting access to the logging data published to CloudWatch Logs.",                        
                    },
                    new Cdklabs.CdkNag.NagPackSuppression()
                    {
                        Id = "AwsSolutions-S1",
                        Reason = "The S3 Bucket has server access logs disabled. The bucket should have server access logging enabled to provide detailed records for the requests that are made to the bucket.",                        
                    }
                }, true);       
        }
    }
}
