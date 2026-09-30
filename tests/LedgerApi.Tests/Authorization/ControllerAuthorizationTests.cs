using System.Reflection;
using LedgerApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Authorization;

public class ControllerAuthorizationTests
{
    // Guards new endpoints: every controller action must name exactly one known scope policy.
    // The fallback policy would still demand a key, but any key at all is not the intended rule.
    [Fact]
    public void EveryControllerAction_RequiresAKnownScopePolicy()
    {
        var actions = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName)
            .ToList();

        var violations = actions
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                || m.GetCustomAttribute<AuthorizeAttribute>()?.Policy is not { } policy
                || !LedgerScopes.All.Contains(policy))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(actions.Count >= 8, $"Expected at least 8 controller actions, found {actions.Count}.");
        Assert.Empty(violations);
    }
}
