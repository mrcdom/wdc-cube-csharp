using Tutorial.Presentation.Modules.Main;
using Tutorial.Presentation.Services;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.TodoMvc;

public sealed class TodoMvcPresenter : CubePresenter<MainPresenter, TodoMvcScope>
{
    private readonly HeaderScope _header = new();
    private readonly TodoListScope _list = new();
    private readonly ClockScope _clock = new();
    private readonly FooterScope _footer = new();

    /// <summary>Every item, in order; the visible list is derived from it in OnBeforeScopeUpdate.</summary>
    private readonly ScopeList<ItemScope> _all;

    private long _userId;

    /// <summary>Bumped on every load: a response that is no longer the latest stays silent.</summary>
    private int _pending;

    private ITimer? _clockTimer;

    public TodoMvcPresenter(MainPresenter app)
        : base(app, new TodoMvcScope())
    {
        _all = new ScopeList<ItemScope>(_list);
        Scope.Header = _header;
        Scope.List = _list; // adopted now, shown or hidden by OnBeforeScopeUpdate
        Scope.Footer = _footer;

        // With many rows changing at once (toggle all on 1000 items), redraw the list instead of each row.
        UpdateManager.Hint<ItemScope>(_list, maxUpdates: 10);
    }

    public override Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new TodoMvcKeys(App, intent);
        keys.ParentSlot(Scope);
        _footer.Showing = keys.Showing;

        if (initialization)
        {
            _header.OnInputChange = Mirror<string>(value => _header.InputValue = value);
            _header.OnInputKeyDown = Action<string>(OnHeaderKeyDown);
            _header.OnToggleAll = Action(OnToggleAll);
            _footer.OnClearCompleted = Action(OnClearCompleted);
            _footer.OnShowAll = Action(() => _footer.Showing = ShowingOptions.All);
            _footer.OnShowActives = Action(() => _footer.Showing = ShowingOptions.Active);
            _footer.OnShowCompleteds = Action(() => _footer.Showing = ShowingOptions.Completed);
            Scope.OnToggleStress = Action(OnToggleStress);
        }

        if (initialization || keys.UserId != _userId)
        {
            _ = ShowUser(keys.UserId);
        }

        return Task.FromResult(true);
    }

    public override void PublishParameters(FlipIntent intent)
    {
        var keys = new TodoMvcKeys(App, intent)
        {
            Showing = _footer.Showing,
            UserId = _userId,
        };
        _ = keys;
    }

    public override void Release()
    {
        _clockTimer?.Dispose();
        _clockTimer = null;
        base.Release();
    }

    /// <summary>Derived state, computed once per flush seeing every change: the filter, counters and visibility.</summary>
    protected override void OnBeforeScopeUpdate()
    {
        var completed = _all.Count(i => i.Completed);
        var pending = _all.Count - completed;

        _list.Items.Assign(_footer.Showing switch
        {
            ShowingOptions.Active => _all.Where(i => !i.Completed),
            ShowingOptions.Completed => _all.Where(i => i.Completed),
            _ => _all,
        });

        _header.AllItemsCompleted = _all.Count > 0 && pending == 0;
        _header.ToggleButtonVisible = _all.Count > 0;
        _footer.Count = pending;
        _footer.ActiveTodoWord = pending == 1 ? "item" : "items";
        _footer.ClearButtonVisible = completed > 0;
        Scope.List = _list.Clock is not null || _list.Items.Count > 0 ? _list : null;
        Scope.Footer = _all.Count > 0 ? _footer : null;
    }

    private async Task ShowUser(long userId)
    {
        _userId = userId;
        Scope.StressMode = userId < 0;
        if (userId < 0)
        {
            _clock.Date = App.Time.GetLocalNow();
            _list.Clock = _clock;
            _clockTimer ??= App.Time.CreateTimer(
                _ => App.Dispatcher.Post(() => _clock.Date = App.Time.GetLocalNow()),
                null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        else
        {
            _list.Clock = null;
            _clockTimer?.Dispose();
            _clockTimer = null;
        }

        await LoadData();
    }

    private async Task LoadData()
    {
        var token = ++_pending;
        IReadOnlyList<TodoItem> todos;
        try
        {
            todos = await App.Service.FetchTodos(_userId);
        }
        catch (Exception caught)
        {
            if (token == _pending)
            {
                Unexpected("Loading the to-do list", caught);
            }

            return;
        }

        if (token != _pending || IsReleased)
        {
            return;
        }

        // Reconcile by identity: the same to-do keeps its row scope, so the view updates it in place.
        var current = _all.ToDictionary(i => i.Id);
        _all.Assign(todos.Select(todo =>
        {
            var row = current.GetValueOrDefault(todo.Id) ?? NewRow();
            row.Identity = todo.Id;
            row.Id = todo.Id;
            row.Title = todo.Title;
            row.Completed = todo.Completed;
            row.Editing = false;
            return row;
        }).ToList());
    }

    private ItemScope NewRow()
    {
        var row = new ItemScope();
        row.OnToggle = Action(() => row.Completed = !row.Completed);
        row.OnEdit = Action(() => Edit(row));
        row.OnBlur = Action<string>(value => Save(row, value));
        row.OnKeyDown = Action<string, string>((value, key) => OnItemKeyDown(row, value, key));
        row.OnDestroy = Action(() => _all.Remove(row));
        return row;
    }

    private void OnHeaderKeyDown(string key)
    {
        if (key == "Escape")
        {
            _header.InputValue = string.Empty;
            return;
        }

        if (key != "Enter")
        {
            return;
        }

        var title = _header.InputValue.Trim();
        _header.InputValue = string.Empty;
        if (title.Length > 0)
        {
            var id = _all.Count == 0 ? 1 : _all.Max(i => i.Id) + 1;
            var row = NewRow();
            row.Identity = id;
            row.Id = id;
            row.Title = title;
            _all.Add(row);
        }
    }

    private void OnToggleAll()
    {
        var check = _all.Any(i => !i.Completed);
        foreach (var item in _all)
        {
            item.Completed = check;
        }
    }

    private void OnClearCompleted() => _all.RemoveAll(i => i.Completed);

    /// <summary>
    /// The place already on screen changes its own state and republishes the address, rather than flipping to
    /// itself: the mode still lands in the URL, and a reload keeps it.
    /// </summary>
    private async Task OnToggleStress() => await ShowUser(_userId < 0 ? 0 : -1);

    private void Edit(ItemScope row)
    {
        foreach (var other in _all)
        {
            other.Editing = ReferenceEquals(other, row);
        }
    }

    private Task OnItemKeyDown(ItemScope row, string value, string key)
    {
        if (key == "Escape")
        {
            row.Editing = false;
        }
        else if (key == "Enter")
        {
            Save(row, value);
        }

        return Task.CompletedTask;
    }

    private void Save(ItemScope row, string value)
    {
        // Escape closes the editor, and the box leaving the screen still fires blur: that one must not save.
        if (!row.Editing)
        {
            return;
        }

        var title = value.Trim();
        if (title.Length == 0)
        {
            _all.Remove(row);
            return;
        }

        row.Title = title;
        row.Editing = false;
    }
}
