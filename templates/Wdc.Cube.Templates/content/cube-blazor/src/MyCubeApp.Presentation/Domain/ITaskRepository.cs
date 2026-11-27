using Wdc.Domain;

namespace MyCubeApp.Domain;

/// <summary>
/// Where the tasks are kept. The presenters only know this contract; the host gives them the one over the database.
/// </summary>
public interface ITaskRepository : IRepository<TaskItem, TaskCriteria, long>;
