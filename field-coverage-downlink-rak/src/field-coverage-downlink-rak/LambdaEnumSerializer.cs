using System;
using System.Text.Json.Serialization;
using Amazon.Lambda.Serialization.SystemTextJson;

namespace field_coverage_lambda
{
    public class LambdaEnumSerializer: DefaultLambdaJsonSerializer
{
    public LambdaEnumSerializer()
        : base(options => options.Converters.Add(new JsonStringEnumConverter())) { }
    
    // Same as above
    // public StringEnumSerializer()
    //     : base(ConfigureJsonSerializerOptions) { }
    //
    // private static void ConfigureJsonSerializerOptions(JsonSerializerOptions options)
    // {
    //     options.Converters.Add(new JsonStringEnumConverter());
    // }
    }
}
