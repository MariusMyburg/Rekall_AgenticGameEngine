using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Immutable, backend-neutral render-graph facts and their deterministic validation result.
/// </summary>
public sealed record RekallAgeHighFidelityRenderGraph(
    IReadOnlyList<RekallAgeHighFidelityRenderResource> Resources,
    IReadOnlyList<RekallAgeHighFidelityRenderPass> Passes,
    IReadOnlyList<RekallAgeHighFidelityRenderDependency> Dependencies,
    long EstimatedBytes,
    IReadOnlyList<RekallAgeHighFidelityRenderGraphDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => !StructuralCodes.Contains(diagnostic.Code));

    public static RekallAgeHighFidelityRenderGraph Create(
        IReadOnlyList<RekallAgeHighFidelityRenderResource> resources,
        IReadOnlyList<RekallAgeHighFidelityRenderPass> passes,
        IReadOnlyList<RekallAgeHighFidelityRenderDependency>? dependencies = null,
        long transientBudgetBytes = long.MaxValue,
        long persistentBudgetBytes = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(passes);

        var orderedResources = resources.ToArray();
        var orderedPasses = passes.OrderBy(pass => pass.Order).ThenBy(pass => pass.Name, StringComparer.Ordinal).ToArray();
        var diagnostics = new List<RekallAgeHighFidelityRenderGraphDiagnostic>();
        ValidateResources(orderedResources, diagnostics);
        ValidatePasses(orderedPasses, diagnostics);
        ValidatePassAccesses(orderedResources, orderedPasses, diagnostics);
        var expectedDependencies = BuildDependencies(orderedPasses);
        var resolvedDependencies = dependencies?.ToArray() ?? expectedDependencies;
        var cycleDependencies = ValidateDependencies(orderedResources, orderedPasses, resolvedDependencies, expectedDependencies, diagnostics);
        ValidateCycles(orderedPasses, cycleDependencies, diagnostics);
        var estimatedBytes = EstimateBytes(orderedResources, diagnostics);
        ValidateBudget(orderedResources, estimatedBytes, transientBudgetBytes, persistentBudgetBytes, diagnostics);

        return new RekallAgeHighFidelityRenderGraph(
            orderedResources,
            orderedPasses,
            resolvedDependencies,
            estimatedBytes,
            diagnostics);
    }

    private static readonly HashSet<string> StructuralCodes = new(StringComparer.Ordinal)
    {
        "REKALL_RENDER_GRAPH_DUPLICATE_RESOURCE",
        "REKALL_RENDER_GRAPH_DUPLICATE_PASS",
        "REKALL_RENDER_GRAPH_INVALID_DIMENSIONS",
        "REKALL_RENDER_GRAPH_DEPTH_COLOR_INCOMPATIBLE",
        "REKALL_RENDER_GRAPH_UNSUPPORTED_FORMAT",
        "REKALL_RENDER_GRAPH_MISSING_RESOURCE",
        "REKALL_RENDER_GRAPH_MISSING_PRODUCER",
        "REKALL_RENDER_GRAPH_READ_BEFORE_WRITE",
        "REKALL_RENDER_GRAPH_INVALID_DEPENDENCY_ENDPOINT",
        "REKALL_RENDER_GRAPH_INVALID_DEPENDENCY_RESOURCE",
        "REKALL_RENDER_GRAPH_INVALID_DEPENDENCY",
        "REKALL_RENDER_GRAPH_DEPENDENCY_MISSING",
        "REKALL_RENDER_GRAPH_CYCLE",
        "REKALL_RENDER_GRAPH_MEMORY_OVERFLOW",
        "REKALL_RENDER_GRAPH_VIEWPORT_DIMENSIONS_MISMATCH"
    };

    private static void ValidateResources(
        IReadOnlyList<RekallAgeHighFidelityRenderResource> resources,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        foreach (var duplicate in resources.GroupBy(resource => resource.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            Add(diagnostics, "REKALL_RENDER_GRAPH_DUPLICATE_RESOURCE", duplicate.Key,
                $"Render resource '{duplicate.Key}' is declared more than once.");
        }

        foreach (var resource in resources)
        {
            if (!TryBytesPerTexel(resource.Format, out _))
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_UNSUPPORTED_FORMAT", resource.Name,
                    $"Render resource '{resource.Name}' uses unsupported format '{resource.Format}'.");
            }

            if (resource.Width <= 0 || resource.Height <= 0 || resource.Layers <= 0)
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_INVALID_DIMENSIONS", resource.Name,
                    $"Render resource '{resource.Name}' must have positive width, height, and layer count.");
            }

            var isDepth = resource.Format.StartsWith("D", StringComparison.OrdinalIgnoreCase);
            var usesColorAttachment = resource.Usage.Any(item => item.Equals("color-attachment", StringComparison.OrdinalIgnoreCase));
            var usesDepthAttachment = resource.Usage.Any(item => item.Equals("depth-attachment", StringComparison.OrdinalIgnoreCase));
            if ((isDepth && usesColorAttachment) || (!isDepth && usesDepthAttachment))
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_DEPTH_COLOR_INCOMPATIBLE", resource.Name,
                    $"Render resource '{resource.Name}' has incompatible format '{resource.Format}' and attachment usage.");
            }
        }
    }

    private static void ValidatePasses(
        IReadOnlyList<RekallAgeHighFidelityRenderPass> passes,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        foreach (var duplicate in passes.GroupBy(pass => pass.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            Add(diagnostics, "REKALL_RENDER_GRAPH_DUPLICATE_PASS", duplicate.Key,
                $"Render pass '{duplicate.Key}' is declared more than once.");
        }
    }

    private static void ValidatePassAccesses(
        IReadOnlyList<RekallAgeHighFidelityRenderResource> resources,
        IReadOnlyList<RekallAgeHighFidelityRenderPass> passes,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        var resourceNames = resources.Select(resource => resource.Name).ToHashSet(StringComparer.Ordinal);
        var resourceLifetimes = resources
            .GroupBy(resource => resource.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Lifetime, StringComparer.Ordinal);
        var producers = passes
            .Where(pass => pass.Enabled)
            .SelectMany(pass => pass.Writes.Select(resource => (Resource: resource, Pass: pass)))
            .GroupBy(item => item.Resource, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Pass).OrderBy(pass => pass.Order).ToArray(), StringComparer.Ordinal);

        foreach (var pass in passes.Where(pass => pass.Enabled))
        {
            foreach (var resource in pass.Reads.Concat(pass.Writes))
            {
                if (!resourceNames.Contains(resource))
                {
                    Add(diagnostics, "REKALL_RENDER_GRAPH_MISSING_RESOURCE", resource,
                        $"Render pass '{pass.Name}' references missing render resource '{resource}'.");
                }
            }

            foreach (var resource in pass.Reads.Where(resourceNames.Contains))
            {
                if (resourceLifetimes[resource].Equals("external", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!producers.TryGetValue(resource, out var writers))
                {
                    Add(diagnostics, "REKALL_RENDER_GRAPH_MISSING_PRODUCER", resource,
                        $"Render resource '{resource}' is read by '{pass.Name}' without a producing pass.");
                    continue;
                }

                var persistentFeedback = resourceLifetimes[resource].Equals("persistent", StringComparison.OrdinalIgnoreCase)
                    && writers.Any(writer => writer.Name.Equals(pass.Name, StringComparison.Ordinal));
                if (!writers.Any(writer => writer.Order < pass.Order) && !persistentFeedback)
                {
                    Add(diagnostics, "REKALL_RENDER_GRAPH_READ_BEFORE_WRITE", resource,
                        $"Render pass '{pass.Name}' reads '{resource}' before an earlier pass writes it.");
                }
            }

            foreach (var resource in pass.Reads.Where(resource => !resourceNames.Contains(resource)))
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_MISSING_PRODUCER", resource,
                    $"Render resource '{resource}' is read by '{pass.Name}' without a producing pass.");
            }
        }
    }

    private static IReadOnlyList<RekallAgeHighFidelityRenderDependency> BuildDependencies(
        IReadOnlyList<RekallAgeHighFidelityRenderPass> passes)
    {
        var writers = new Dictionary<string, RekallAgeHighFidelityRenderPass>(StringComparer.Ordinal);
        var dependencies = new List<RekallAgeHighFidelityRenderDependency>();
        foreach (var pass in passes.Where(pass => pass.Enabled))
        {
            foreach (var resource in pass.Reads)
            {
                if (writers.TryGetValue(resource, out var producer))
                {
                    dependencies.Add(new RekallAgeHighFidelityRenderDependency(producer.Name, pass.Name, resource));
                }
            }

            foreach (var resource in pass.Writes)
            {
                writers[resource] = pass;
            }
        }

        return dependencies;
    }

    private static IReadOnlyList<RekallAgeHighFidelityRenderDependency> ValidateDependencies(
        IReadOnlyList<RekallAgeHighFidelityRenderResource> resources,
        IReadOnlyList<RekallAgeHighFidelityRenderPass> passes,
        IReadOnlyList<RekallAgeHighFidelityRenderDependency> dependencies,
        IReadOnlyList<RekallAgeHighFidelityRenderDependency> expectedDependencies,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        var resourcesByName = resources
            .GroupBy(resource => resource.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var passesByName = passes
            .GroupBy(pass => pass.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var cycleDependencies = new List<RekallAgeHighFidelityRenderDependency>();
        foreach (var dependency in dependencies)
        {
            if (!passesByName.TryGetValue(dependency.ProducerPass, out var producers)
                || !passesByName.TryGetValue(dependency.ConsumerPass, out var consumers)
                || producers.Length != 1
                || consumers.Length != 1)
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_INVALID_DEPENDENCY_ENDPOINT", dependency.Resource,
                    $"Render dependency '{dependency.ProducerPass}' -> '{dependency.ConsumerPass}' has an unknown or ambiguous pass endpoint.");
                continue;
            }

            cycleDependencies.Add(dependency);
            var producer = producers[0];
            var consumer = consumers[0];
            if (!resourcesByName.ContainsKey(dependency.Resource)
                || !producer.Writes.Contains(dependency.Resource, StringComparer.Ordinal)
                || !consumer.Reads.Contains(dependency.Resource, StringComparer.Ordinal))
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_INVALID_DEPENDENCY_RESOURCE", dependency.Resource,
                    $"Render dependency '{dependency.ProducerPass}' -> '{dependency.ConsumerPass}' does not match declared resource access for '{dependency.Resource}'.");
                continue;
            }

            if (!expectedDependencies.Contains(dependency))
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_INVALID_DEPENDENCY", dependency.Resource,
                    $"Render dependency '{dependency.ProducerPass}' -> '{dependency.ConsumerPass}' does not match the declared pass order for '{dependency.Resource}'.");
                continue;
            }
        }

        foreach (var expected in expectedDependencies)
        {
            if (!dependencies.Contains(expected))
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_DEPENDENCY_MISSING", expected.Resource,
                    $"Render dependency '{expected.ProducerPass}' -> '{expected.ConsumerPass}' is required for '{expected.Resource}'.");
            }
        }

        return cycleDependencies;
    }

    private static void ValidateCycles(
        IReadOnlyList<RekallAgeHighFidelityRenderPass> passes,
        IReadOnlyList<RekallAgeHighFidelityRenderDependency> dependencies,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        var passNames = passes.Select(pass => pass.Name).ToHashSet(StringComparer.Ordinal);
        var outgoing = passNames.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var dependency in dependencies.Where(dependency => passNames.Contains(dependency.ProducerPass) && passNames.Contains(dependency.ConsumerPass)))
        {
            outgoing[dependency.ProducerPass].Add(dependency.ConsumerPass);
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pass in passNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (HasCycle(pass, outgoing, visiting, visited))
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_CYCLE", pass,
                    "Render graph dependencies contain a cycle.");
                return;
            }
        }
    }

    private static bool HasCycle(
        string pass,
        IReadOnlyDictionary<string, List<string>> outgoing,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visited.Contains(pass))
        {
            return false;
        }

        if (!visiting.Add(pass))
        {
            return true;
        }

        foreach (var next in outgoing[pass])
        {
            if (HasCycle(next, outgoing, visiting, visited))
            {
                return true;
            }
        }

        visiting.Remove(pass);
        visited.Add(pass);
        return false;
    }

    private static long EstimateBytes(
        IReadOnlyList<RekallAgeHighFidelityRenderResource> resources,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        long total = 0;
        foreach (var resource in resources.Where(resource => !resource.Lifetime.Equals("external", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryBytesPerTexel(resource.Format, out var bytesPerTexel))
            {
                continue;
            }

            try
            {
                var bytes = checked((long)resource.Width * resource.Height * resource.Layers * bytesPerTexel);
                total = checked(total + bytes);
            }
            catch (OverflowException)
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_MEMORY_OVERFLOW", resource.Name,
                    $"Render resource '{resource.Name}' exceeds supported memory arithmetic.");
            }
        }

        return total;
    }

    private static void ValidateBudget(
        IReadOnlyList<RekallAgeHighFidelityRenderResource> resources,
        long estimatedBytes,
        long transientBudgetBytes,
        long persistentBudgetBytes,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        try
        {
            var budget = checked(transientBudgetBytes + persistentBudgetBytes);
            if (estimatedBytes > budget)
            {
                Add(diagnostics, "REKALL_RENDER_GRAPH_MEMORY_BUDGET_EXCEEDED", "graph",
                    $"Planned render resources require {estimatedBytes} bytes but the resolved budget is {budget} bytes.");
            }
        }
        catch (OverflowException)
        {
            // An unbounded caller-provided budget cannot constrain the graph.
        }
    }

    private static bool TryBytesPerTexel(string format, out int bytesPerTexel)
    {
        bytesPerTexel = format.Trim().ToUpperInvariant() switch
        {
            "R8_UNORM" => 1,
            "R16G16_SFLOAT" => 4,
            "R32_UINT" => 4,
            "D32_SFLOAT" => 4,
            "R8G8B8A8_UNORM" => 4,
            "R16G16B16A16_SFLOAT" => 8,
            _ => 0
        };
        return bytesPerTexel > 0;
    }

    private static void Add(
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics,
        string code,
        string target,
        string message)
    {
        if (!diagnostics.Any(item => item.Code == code && item.Target == target))
        {
            diagnostics.Add(new RekallAgeHighFidelityRenderGraphDiagnostic(code, target, message));
        }
    }
}
