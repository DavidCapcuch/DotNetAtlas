using System.Reflection;
using DotNetAtlas.Api.Common;
using DotNetAtlas.Application.Common;
using DotNetAtlas.Domain.Common;
using DotNetAtlas.Infrastructure.Common;
using DotNetAtlas.Test.Framework.Kafka;

namespace DotNetAtlas.ArchitectureTests;

public abstract class BaseTest
{
    protected static readonly Assembly DomainAssembly = typeof(ValueObject).Assembly;
    protected static readonly Assembly ApplicationAssembly = typeof(ApplicationDependencyInjection).Assembly;
    protected static readonly Assembly InfrastructureAssembly = typeof(InfrastructureDependencyInjection).Assembly;
    protected static readonly Assembly PresentationAssembly = typeof(ApiDependencyInjection).Assembly;

    protected static readonly Assembly TestFrameworkAssembly = typeof(KafkaTestContainer).Assembly;
}
