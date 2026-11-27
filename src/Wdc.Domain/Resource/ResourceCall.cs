namespace Wdc.Domain;

/// <summary>
/// One operation of the presentation on the data, named by what it does with the resource, in the spirit of the HTTP
/// methods: a new instance per use, the request in its constructor's parameters, the response from <see cref="Run"/>.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Suffix</term><description>Base and transaction</description></listheader>
/// <item><term><c>Query</c></term><description><see cref="ResourceQuery{TResult}"/>: none of its own; it reads in the
/// caller's, if any.</description></item>
/// <item><term><c>Create</c></term><description><see cref="ResourceCreate{TResult}"/> (POST): <see cref="Run"/> opens
/// one.</description></item>
/// <item><term><c>Save</c></term><description><see cref="ResourceSave{TResult}"/> (PUT, idempotent): <see cref="Run"/>
/// opens one.</description></item>
/// <item><term><c>Update</c></term><description><see cref="ResourceUpdate{TResult}"/> (PATCH): <see cref="Run"/> opens
/// one.</description></item>
/// <item><term><c>Delete</c></term><description><see cref="ResourceDelete{TResult}"/> (DELETE, idempotent):
/// <see cref="Run"/> opens one.</description></item>
/// </list>
/// <para>What fits none gets a verb of its own and extends this class directly. The write verbs are closed: no other
/// assembly can extend <see cref="ResourceWrite{TResult}"/>, so every write runs in a transaction.</para>
/// </remarks>
public abstract class ResourceCall<TResult>
{
    public abstract Task<TResult> Run(CancellationToken cancellationToken = default);
}
