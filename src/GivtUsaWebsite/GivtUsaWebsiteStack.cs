using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace GivtUsaWebsite
{
    public class GivtUsaWebsiteStack : Stack
    {
        internal GivtUsaWebsiteStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            var vpc = new Vpc(this, "vpc", new VpcProps
            {
                NatGateways = 0, // why tho?
                SubnetConfiguration = new[]
                {
                    new SubnetConfiguration
                    {
                        SubnetType = SubnetType.PUBLIC,
                        CidrMask = 24,
                        Name = "public-subnet"
                    }
                }
            });
            var securityGroup = new SecurityGroup(this, "security-group", new SecurityGroupProps
            {
                Vpc = vpc,
                AllowAllOutbound = true,
                SecurityGroupName = "security-group-basic-traffic"
            });

            securityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "allow traffic on http port");

            var ec2 = new Instance_(this, "givt-usa-webiste", new InstanceProps
            {
                SecurityGroup = securityGroup,
                Vpc = vpc,
                KeyName = "webserver-keypair",
                InstanceType = new InstanceType("t3a.micro"),
                InstanceName = "givt-usa-website",
                MachineImage = MachineImage.GenericLinux(new Dictionary<string, string>(new[] { new KeyValuePair<string, string>("us-east-1", "ami-02abe9f0014128124") }))
            });


            var origin = new HttpOrigin(ec2.InstancePublicDnsName, new HttpOriginProps
            {
                ProtocolPolicy = OriginProtocolPolicy.HTTP_ONLY
            });

            var cloudfront = new Distribution(this, "givt-team-usa-distribuion", new DistributionProps
            {
                DomainNames = new[] { "givt.app" },
                HttpVersion = HttpVersion.HTTP2,
                PriceClass = PriceClass.PRICE_CLASS_100,
                Certificate = Certificate.FromCertificateArn(this, "certificate", "arn:aws:acm:us-east-1:599728923386:certificate/c642f334-5183-494e-b664-bbf98750afd0"),
                DefaultBehavior = new BehaviorOptions
                {
                    Origin = origin,
                    ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                    AllowedMethods = AllowedMethods.ALLOW_ALL,
                    Compress = true,
                    CachedMethods = CachedMethods.CACHE_GET_HEAD_OPTIONS,
                    CachePolicy = new CachePolicy(this, "caching-policy-us-website", new CachePolicyProps
                    {
                        Comment = "Wordpress us website cache policy",
                        QueryStringBehavior = CacheQueryStringBehavior.All(),
                        HeaderBehavior = CacheHeaderBehavior.AllowList("Host", "Origin", "CloudFront-Forwarded-Proto"),
                        CookieBehavior = CacheCookieBehavior.All(),
                        MinTtl = Duration.Minutes(5),
                        DefaultTtl = Duration.Days(7),
                        MaxTtl = Duration.Days(31),
                        EnableAcceptEncodingGzip = true,
                        EnableAcceptEncodingBrotli = true,
                        CachePolicyName = "wordpress-usa-production-cache-policy"
                    })
                }
            });

            var headerOptions = new CachePolicy(this, "CustomHeadersPassed", new CachePolicyProps
            {
                CachePolicyName = "custom-headers-passed",
                QueryStringBehavior = CacheQueryStringBehavior.All(),
                HeaderBehavior = CacheHeaderBehavior.AllowList("Host", "Origin", "Referer", "User-Agent", "CloudFront-Forwarded-Proto"),
                CookieBehavior = CacheCookieBehavior.AllowList("comment_author_*", "comment_author_email_*", "comment_author_url_*", "wordpress_logged_in_*", "wordpress_test_cookie", "wp-settings-*", "PHPSESSID", "wordpress_*", "wordpress_sec_*")
            });

            var originRequestPolicy = new OriginRequestPolicy(this, "OriginRequestPolicy", new OriginRequestPolicyProps
            {
                CookieBehavior = OriginRequestCookieBehavior.All(),
                QueryStringBehavior = OriginRequestQueryStringBehavior.All(),
                HeaderBehavior = OriginRequestHeaderBehavior.All(),
                OriginRequestPolicyName = "custom-header-passed-nocache"
            });

            AddBehaviourForPath("/wp-login.php", cloudfront, origin, headerOptions, originRequestPolicy);
            AddBehaviourForPath("/wp-admin/*", cloudfront, origin, headerOptions, originRequestPolicy);
            AddBehaviourForPath("/wp-json/*", cloudfront, origin, headerOptions, originRequestPolicy);
            AddBehaviourForPath("/contact/", cloudfront, origin, headerOptions, originRequestPolicy);
            // AddBehaviourForPath("/.well-known/*", cloudfront, origin, headerOptions, originRequestPolicy);
            AddBehaviourForPath("/wp-cron.php", cloudfront, origin, headerOptions, originRequestPolicy);
            AddBehaviourForPath("/xmlrpc.php", cloudfront, origin, headerOptions, originRequestPolicy);
            AddBehaviourForPath("/wp-trackback.php", cloudfront, origin, headerOptions, originRequestPolicy);
            AddBehaviourForPath("/wp-signup.php", cloudfront, origin, headerOptions, originRequestPolicy);

            var bucket = new Bucket(this, "AssetsBucket", new BucketProps
            {
                AccessControl = BucketAccessControl.PRIVATE,
                Encryption = BucketEncryption.S3_MANAGED,
                EnforceSSL = true,
                PublicReadAccess = false,
                AutoDeleteObjects = true,
                RemovalPolicy = RemovalPolicy.DESTROY,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL
            });

            var originAccessForBucket = new OriginAccessIdentity(this, "MarketingUSABackupBucket");

            bucket.GrantRead(originAccessForBucket);

            var s3HeaderOptions = new CachePolicy(this, "S3Headers", new CachePolicyProps
            {
                CachePolicyName = "custom-headers-passed-for-s3",
                MinTtl = Duration.Days(30),
                DefaultTtl = Duration.Days(45),
                MaxTtl = Duration.Days(60),
                QueryStringBehavior = CacheQueryStringBehavior.All(),
                HeaderBehavior = CacheHeaderBehavior.AllowList("Origin", "Access-Control-Request-Headers", "Access-Control-Request-Method"),
                CookieBehavior = CacheCookieBehavior.None()
            });

            AddS3BehaviourForPath(bucket, originAccessForBucket, "/apple-app-site-association", cloudfront, s3HeaderOptions);
            AddS3BehaviourForPath(bucket, originAccessForBucket, "/.well-known/assetlinks.json", cloudfront, s3HeaderOptions);
        }
        private void AddS3BehaviourForPath(Bucket bucket, OriginAccessIdentity identity, string path,
                    Distribution cloudFront, CachePolicy cachePolicy, string s3Path = null)
        {
            var origin = new S3Origin(bucket, new S3OriginProps
            {
                OriginAccessIdentity = identity,
                OriginPath = s3Path
            });

            cloudFront.AddBehavior(path, origin, new BehaviorOptions
            {
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
                CachedMethods = CachedMethods.CACHE_GET_HEAD_OPTIONS,
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                CachePolicy = cachePolicy,
                Compress = true
            });
        }
        private void AddBehaviourForPath(string path, Distribution cloudfront, IOrigin origin, CachePolicy cachePolicy, IOriginRequestPolicy originRequestPolicy = null)
        {

        }
    }
}