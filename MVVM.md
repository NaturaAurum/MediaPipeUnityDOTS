# Common MVVM + Rx Rules

## Purpose

This document defines shared conventions for using MVVM and Rx.
These rules are not limited to a specific screen; they provide consistent decision-making criteria across the UI layer.

The core goals are:

- Do not make business decisions in the View.
- Do not expose Entities through the public APIs of Views or ViewModels.
- Models own meaningful state and express mutable state through Rx streams.
- ViewModels compose Model streams with operators and expose them in the form the UI needs.
- Views, as the final consumers, manage subscription lifetimes with `AddTo(this)`.

## Separation of Responsibilities

### Entity

An Entity is data from an external boundary, such as an API response, a repository record, or an external SDK DTO.
Avoid exposing Entities directly through the public APIs of Views, ViewModels, or Models.

```csharp
// 피하고 싶은 형태
public sealed class UserProfileViewModel
{
    public UserProfileResponseEntity Entity { get; }
}
```

Convert Entities into UI or domain state at boundaries such as Repositories, UseCases, or Mappers.

### Model

A Model owns state and domain rules; it is not a copy of an Entity.
Where possible, represent changing state with types such as `ReactiveProperty<T>`, `ReadOnlyReactiveProperty<T>`, or `Observable<T>`.

```csharp
using System;
using System.Collections.Generic;
using R3;

public sealed class ProfileRecordModel
{
    private readonly ReactiveProperty<IReadOnlyList<RecordItemState>> _items = new(Array.Empty<RecordItemState>());
    private readonly ReactiveProperty<int> _completedCount = new(0);
    private readonly ReactiveProperty<bool> _isLoading = new(false);

    public ReadOnlyReactiveProperty<IReadOnlyList<RecordItemState>> Items => _items;
    public ReadOnlyReactiveProperty<int> CompletedCount => _completedCount;
    public ReadOnlyReactiveProperty<bool> IsLoading => _isLoading;

    public void BeginLoading()
    {
        _isLoading.Value = true;
    }

    public void Apply(ProfileRecordSnapshot snapshot)
    {
        _items.Value = snapshot.Items;
        _completedCount.Value = snapshot.CompletedCount;
        _isLoading.Value = false;
    }
}
```

As a rule, Models do not directly `Subscribe` to other streams.
They focus on owning state and providing methods to change it.

### ViewModel

A ViewModel exposes Model streams in a form the UI can subscribe to directly.
Restrict write operations to meaningful command methods rather than public setters.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using R3;

public sealed class ProfileRecordViewModel
{
    private readonly ProfileRecordModel _model;
    private readonly ReactiveProperty<RecordTab> _selectedTab = new(RecordTab.Personal);

    public ReadOnlyReactiveProperty<RecordTab> SelectedTab => _selectedTab;
    public Observable<IReadOnlyList<RecordItemState>> VisibleItems { get; }
    public Observable<float> Progress { get; }
    public Observable<bool> CanEdit { get; }

    public ProfileRecordViewModel(ProfileRecordModel model)
    {
        _model = model;

        VisibleItems = _selectedTab
            .CombineLatest(_model.Items, SelectVisibleItems)
            .DistinctUntilChanged();

        Progress = _model.CompletedCount
            .Select(CalculateProgress)
            .DistinctUntilChanged();

        CanEdit = _model.IsLoading
            .Select(isLoading => !isLoading)
            .DistinctUntilChanged();
    }

    public void SelectTab(RecordTab tab)
    {
        _selectedTab.Value = tab;
    }

    private static IReadOnlyList<RecordItemState> SelectVisibleItems(
        RecordTab tab,
        IReadOnlyList<RecordItemState> items)
    {
        return items.Where(item => item.Tab == tab).ToArray();
    }

    private static float CalculateProgress(int completedCount)
    {
        return ProfileRecordProgressCalculator.Calculate(completedCount);
    }
}
```

As a rule, ViewModels do not directly `Subscribe` either.
Compose streams with operators such as `CombineLatest`, `Select`, `Where`, and `Merge`, and defer the final subscription to the View.

### View

A View subscribes to ViewModel streams and renders the UI.
It does not read Entities or perform calculations based on the structure of API responses.

```csharp
using R3;

private void Bind(ProfileRecordViewModel viewModel)
{
    viewModel.VisibleItems
        .Subscribe(RenderVisibleItems)
        .AddTo(this);

    viewModel.Progress
        .Subscribe(RenderProgress)
        .AddTo(this);

    viewModel.CanEdit
        .Subscribe(SetInteractable)
        .AddTo(this);
}
private void RenderProgress(float progress)
{
    progressSlider.value = progress;
}
```

## Patterns to Avoid

### Models or Data Types That Mirror Entities

```csharp
public sealed class RecordItemModel
{
    public string Id { get; }
    public string Title { get; }
    public int RequiredLevel { get; }
    public bool IsUnlocked { get; }

    public RecordItemModel(RecordItemEntity entity)
    {
        Id = entity.Id;
        Title = entity.Title;
        RequiredLevel = entity.RequiredLevel;
        IsUnlocked = entity.IsUnlocked;
    }
}
```

Although this structure avoids using the Entity directly, it is effectively a DTO copy.
If a Model is not responsible for state or rules, it provides little of MVVM's value.

### Storing an Entity Directly in a Model

```csharp
public sealed class RecordModel
{
    public RecordResponseEntity Entity { get; private set; }

    public void Set(RecordResponseEntity entity)
    {
        Entity = entity;
    }
}
```

An Entity may appear to be hidden inside a Model, but exposing it through a public API still leaks the boundary.
A Model should hold meaningful screen or domain state, not an Entity.

### Models with Properties but No Change Streams

```csharp
public sealed class StepProgressModel
{
    public int CurrentStep { get; private set; }
    public int RemainingCount { get; private set; }

    public void UpdateProgress(int currentStep, int remainingCount)
    {
        CurrentStep = currentStep;
        RemainingCount = remainingCount;
    }
}
```

When values live in the Model without change notifications, the View or ViewModel must manually coordinate when to call methods such as `Refresh()`, `Setup()`, or `Render()`.
In an Rx-based architecture, prefer representing changing state itself as streams.

### Triggering a Full Re-render with a Single Updated Stream

```csharp
using System.Collections.Generic;
using R3;

public sealed class RecordModel
{
    private readonly Subject<Unit> _updated = new();

    public IReadOnlyList<RecordItemState> Items { get; private set; }
    public Observable<Unit> Updated => _updated;

    public void SetItems(IReadOnlyList<RecordItemState> items)
    {
        Items = items;
        _updated.OnNext(Unit.Default);
    }
}
```

`Updated` does not express which value changed or why.
Subscribers end up rereading all state and redrawing the entire UI.
Where possible, split this into streams for individual pieces of state.

## Guidelines for Rx Operators

Rx is not used merely to put values into `ReactiveProperty` instances.
Its purpose is to declaratively combine multiple pieces of state into the state the UI needs.

Common operator use cases include:

- `Select`: Transform Model values into values for UI display.
- `Where`: Allow only valid events through.
- `CombineLatest`: Combine two or more pieces of state into a single UI state.
- `DistinctUntilChanged`: Reduce unnecessary re-rendering caused by identical values.
- `ThrottleFirst`: Limit inputs repeated within a short interval, such as rapid button clicks.
- `Merge`: Combine multiple input events into a single command stream.
- `Switch`: Use when only the latest request's results should be reflected in the UI.

Using more operators is not a goal in itself.
If moving conditions into a stream makes the code harder to read, break it into meaningfully named streams or private methods.

### Lambdas Inside Operators

Lambdas are fine for simple transformations that are immediately understandable, such as `Select(isLoading => !isLoading)`.
However, when the logic includes branching, calculations, collection searches, or domain decisions, extract it into a method whose name communicates the intent.

```csharp
// 피하고 싶은 형태
HasReceivableReward = rewards
    .Select(items => items.Any(item => item.CanReceive && !item.IsExpired))
    .DistinctUntilChanged();
```

```csharp
// 지향하는 형태
HasReceivableReward = rewards
    .Select(HasReceivableRewardItem)
    .DistinctUntilChanged();

private static bool HasReceivableRewardItem(IReadOnlyList<RewardState> rewards)
{
    return rewards.Any(reward => reward.CanReceive && !reward.IsExpired);
}
```

## Using Observable Extensions and Helper Operators

Make active use of R3's built-in operators and helper extension methods.
Rather than writing verbose or repetitive expressions, use clear declarative operators to express intent:

- `WhereNotNull()`: Allow only non-null values through.
- `Select(x => condition)`: Transform values into bool flags or presentation models.
- `ThrottleFirst(TimeSpan)`: Limit rapid repeated inputs such as button clicks.
- `Debounce(TimeSpan)`: Delay emission until a specified quiet period has elapsed (e.g., search text input).

```csharp
using R3;

public sealed class RecordViewModel
{
    public Observable<bool> IsPersonalTab { get; }
    public Observable<bool> CanInput { get; }

    public RecordViewModel(ReadOnlyReactiveProperty<RecordTab> selectedTab, ReadOnlyReactiveProperty<bool> isLoading)
    {
        IsPersonalTab = selectedTab
            .Select(tab => tab == RecordTab.Personal)
            .DistinctUntilChanged();

        CanInput = isLoading
            .Select(loading => !loading)
            .DistinctUntilChanged();
    }
}
```

When binding UI events (such as button clicks or value changes in UI Toolkit / uGUI), transform the event to command parameters declaratively:

```csharp
using System;
using R3;

private void Bind(RecordViewModel viewModel)
{
    personalButton.OnClickAsObservable()
        .Select(_ => RecordTab.Personal)
        .Merge(partnerButton.OnClickAsObservable().Select(_ => RecordTab.Partner))
        .ThrottleFirst(TimeSpan.FromMilliseconds(500))
        .Subscribe(viewModel.SelectTab)
        .AddTo(this);
}
```

## Guidelines for Subscribe and AddTo
### Avoid Lambdas in Subscribe Where Possible

Writing handling logic directly inside a `Subscribe` lambda quickly makes binding code lengthy.
In particular, extract multi-line operations such as updating the UI, showing dialogs, or rebuilding lists into named methods.

```csharp
// 피하고 싶은 형태
viewModel.CanEdit
    .Subscribe(canEdit =>
    {
        saveButton.interactable = canEdit;
        cancelButton.interactable = canEdit;
    })
    .AddTo(this);
```

```csharp
// 지향하는 형태
viewModel.CanEdit
    .Subscribe(SetButtonsInteractable)
    .AddTo(this);

private void SetButtonsInteractable(bool canEdit)
{
    saveButton.interactable = canEdit;
    cancelButton.interactable = canEdit;
}
```

As an exception, a simple one-line command forwarding lambda may be acceptable.
Even then, prefer declarative mapping (`Select(_ => ...)`) or a method group if either can replace it.

### Tie Subscriptions to the View Lifecycle with AddTo(this)

When subscribing in a `MonoBehaviour`, use `AddTo(this)` by default.
R3's `AddTo(this)` (from `R3.Unity`) disposes subscriptions according to that `MonoBehaviour`'s `OnDestroy` lifecycle, ensuring UI bindings are automatically cleaned up when the View is destroyed.
Subscriptions can also be tied to a `CancellationToken` via `AddTo(destroyCancellationToken)`.

```csharp
private void Bind(RecordViewModel viewModel)
{
    viewModel.VisibleItems
        .Subscribe(RenderVisibleItems)
        .AddTo(this);

    viewModel.Progress
        .Subscribe(RenderProgress)
        .AddTo(this);
}
```

## Disposable Guidelines for Models and ViewModels

Where possible, avoid adding `CompositeDisposable` or `IDisposable` to Models and ViewModels.
Models own state, and ViewModels compose and expose streams through operators.
The actual `Subscribe` calls belong in the View as the final consumer, with lifetimes tied to the `MonoBehaviour` through `AddTo(this)`.

```csharp
// 피하고 싶은 형태
using System;
using R3;

public sealed class RecordViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public ReadOnlyReactiveProperty<float> Progress { get; }

    public RecordViewModel(RecordModel model)
    {
        Progress = model.CompletedCount
            .Select(CalculateProgress)
            .ToReadOnlyReactiveProperty()
            .AddTo(_disposables);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
```

```csharp
// 지향하는 형태
using R3;

public sealed class RecordViewModel
{
    public Observable<float> Progress { get; }

    public RecordViewModel(RecordModel model)
    {
        Progress = model.CompletedCount
            .Select(CalculateProgress)
            .DistinctUntilChanged();
    }
}

private void Bind(RecordViewModel viewModel)
{
    viewModel.Progress
        .Subscribe(RenderProgress)
        .AddTo(this);
}
```

Also avoid direct subscriptions between Models, or between Models and ViewModels, wherever possible.
When state dependencies are needed, compose streams with operators and defer the final subscription to the View.

Exceptions are allowed when the implementation genuinely requires them.
Examples include bridging external SDK events into Model state or ensuring that asynchronous results are applied to an internal cache.
In these cases, explain why the internal subscription is necessary in a comment or the PR description, and make disposal ownership explicit.
Outside such exceptions, question the need to add a `CompositeDisposable` to a Model or ViewModel.

## Checklist

- If a Model has almost the same field list as an Entity, reconsider its design.
- If a View or ViewModel public API exposes an `*Entity`, treat it as a boundary leak.
- First consider whether mutable Model state can be expressed through Rx streams.
- If a single `Updated` stream triggers a full re-render, check whether it can be split into streams for individual pieces of state.
- If a View calculates or branches based on API responses, move that logic into a Model or ViewModel.
- Avoid public setters that allow arbitrary external state changes; restrict changes to meaningful methods or commands.
- Use lambdas inside operators only for simple transformations; extract decisions and calculations into methods.
- Avoid lambdas in `Subscribe` where possible, and use method groups instead.
- By default, tie streams subscribed to in a `MonoBehaviour` to `OnDestroy` with `AddTo(this)`.
- If a Model or ViewModel gains a `CompositeDisposable` or `IDisposable`, first check whether an internal subscription is genuinely necessary as an exception.
- Prefer operator composition over direct subscriptions between Models and ViewModels, and perform the final subscription in the View.
