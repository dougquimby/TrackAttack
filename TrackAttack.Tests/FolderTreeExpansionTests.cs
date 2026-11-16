using System.Collections;
using System.IO;
using System.Reflection;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Views;

namespace TrackAttack.Tests;

public sealed class FolderTreeExpansionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"TrackAttack-folder-tree-{Guid.NewGuid():N}");
    private static readonly Type NodeType = typeof(QuizSetupView).Assembly.GetType("TrackAttack.Views.MusicFolderNode", true)!;
    private static readonly MethodInfo Create = NodeType.GetMethod("Create")!;
    private static readonly MethodInfo SetInitialExpansion = typeof(QuizSetupView)
        .GetMethod("SetInitialFirstRootExpansion", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void FirstRootWithChildrenStartsExpandedButOtherNodesStayCollapsed()
    {
        var first = CreateRoot("first", withChild: true);
        var second = CreateRoot("second", withChild: true);
        SetExpansion([first, second], null);

        Assert.True(IsExpanded(first));
        Assert.False(IsExpanded(second));
        Assert.False(IsExpanded(Assert.Single(Children(first))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RefreshRetainsFirstRootsExpansion(bool expanded)
    {
        var previous = CreateRoot("first", withChild: true);
        NodeType.GetProperty("IsExpanded")!.SetValue(previous, expanded);
        var current = CreateRoot("first", withChild: true);
        SetExpansion([current], previous);

        Assert.Equal(expanded, IsExpanded(current));
    }

    [Fact]
    public void NewOrPreviouslyChildlessRootStartsExpandedWhenItHasChildren()
    {
        var previous = CreateRoot("first", withChild: false);
        var current = CreateRoot("first", withChild: true);
        SetExpansion([current], previous);
        Assert.True(IsExpanded(current));

        var replacement = CreateRoot("replacement", withChild: true);
        SetExpansion([replacement], previous);
        Assert.True(IsExpanded(replacement));
    }

    [Fact]
    public void EmptyAndChildlessTreesStayCollapsed()
    {
        var root = CreateRoot("first", withChild: false);
        SetExpansion([], null);
        SetExpansion([root], null);
        Assert.False(IsExpanded(root));
    }

    private object CreateRoot(string name, bool withChild)
    {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(withChild ? Path.Combine(path, "child", "grandchild") : path);
        var issues = new List<LibraryScanIssue>();
        var node = Create.Invoke(null, [path, Array.Empty<MusicFolderRule>(), issues])!;
        Assert.Empty(issues);
        return node;
    }

    private static void SetExpansion(object[] nodes, object? previous)
    {
        var typedNodes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(NodeType))!;
        foreach (var node in nodes)
            typedNodes.Add(node);
        SetInitialExpansion.Invoke(null, [typedNodes, previous]);
    }

    private static bool IsExpanded(object node) => (bool)NodeType.GetProperty("IsExpanded")!.GetValue(node)!;
    private static IEnumerable<object> Children(object node)
        => ((IEnumerable)NodeType.GetProperty("Children")!.GetValue(node)!).Cast<object>();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
