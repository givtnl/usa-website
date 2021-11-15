using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GivtUsaWebsite
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new GivtUsaWebsiteStack(app, "GivtUsaWebsiteStack", new StackProps
            {
                StackName = "givt-usa-website"
            });
            app.Synth();
        }
    }
}
