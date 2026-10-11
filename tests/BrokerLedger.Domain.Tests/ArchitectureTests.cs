using BrokerLedger.Domain;
using NetArchTest.Rules;

namespace BrokerLedger.Domain.Tests;

/// <summary>Domena to czysta logika: zadnej bazy, HTTP ani frameworka.</summary>
public class ArchitectureTests
{
    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("System.Net.Http")]
    [InlineData("Npgsql")]
    [InlineData("Confluent.Kafka")]
    public void Domain_does_not_depend_on_infrastructure(string forbidden)
    {
        var result = Types.InAssembly(typeof(Currency).Assembly)
            .ShouldNot().HaveDependencyOn(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_classes_are_sealed_unless_abstract()
    {
        var result = Types.InAssembly(typeof(Currency).Assembly)
            .That().AreClasses().And().AreNotAbstract()
            .Should().BeSealed()
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
