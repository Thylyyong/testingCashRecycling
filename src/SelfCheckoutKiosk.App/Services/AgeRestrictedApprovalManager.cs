using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Represents a pending customer age-restricted item approval request.
/// </summary>
public sealed class AgeRestrictedApprovalRequest
{
    public Product Product { get; }
    public DateTimeOffset RequestedAtUtc { get; } = DateTimeOffset.UtcNow;
    public TaskCompletionSource<bool> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AgeRestrictedApprovalRequest(Product product)
    {
        Product = product;
    }
}

/// <summary>
/// Central manager for age-restricted item attendant approval workflows.
/// Bridges customer scanning with Admin Diagnostics attendant approvals.
/// </summary>
public sealed class AgeRestrictedApprovalManager : INotifyPropertyChanged
{
    private static readonly Lazy<AgeRestrictedApprovalManager> _lazy = new(() => new AgeRestrictedApprovalManager());
    public static AgeRestrictedApprovalManager Instance => _lazy.Value;

    private AgeRestrictedApprovalRequest? _pendingRequest;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? PendingRequestChanged;
    public event Action<Product>? OnProductApproved;
    public event Action<Product>? OnProductRejected;

    public AgeRestrictedApprovalRequest? PendingRequest
    {
        get => _pendingRequest;
        private set
        {
            if (_pendingRequest != value)
            {
                _pendingRequest = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPendingRequest));
                OnPropertyChanged(nameof(PendingItemName));
                OnPropertyChanged(nameof(PendingItemSku));
                PendingRequestChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool HasPendingRequest => _pendingRequest != null;

    public string PendingItemName => _pendingRequest?.Product.Name ?? string.Empty;

    public string PendingItemSku => _pendingRequest?.Product.Sku ?? string.Empty;

    /// <summary>
    /// Initiates an approval request for an age-restricted item.
    /// Returns a Task that completes with true (Approved) or false (Rejected).
    /// </summary>
    public Task<bool> RequestApprovalAsync(Product product)
    {
        // Cancel any prior unhandled request
        _pendingRequest?.CompletionSource.TrySetResult(false);

        var request = new AgeRestrictedApprovalRequest(product);
        PendingRequest = request;

        return request.CompletionSource.Task;
    }

    /// <summary>
    /// Attendant approves the pending age-restricted item.
    /// Directly adds the approved product to the cart service once and completes the task.
    /// </summary>
    public void Approve()
    {
        if (_pendingRequest != null)
        {
            var req = _pendingRequest;
            PendingRequest = null;

            // Add the approved item directly to the centralized CartService instance exactly once
            App.CartServiceInstance.AddItem(req.Product.Name, req.Product.Sku, req.Product.Price, 1);

            req.CompletionSource.TrySetResult(true);
            OnProductApproved?.Invoke(req.Product);
        }
    }

    /// <summary>
    /// Attendant rejects the pending age-restricted item.
    /// </summary>
    public void Reject()
    {
        if (_pendingRequest != null)
        {
            var req = _pendingRequest;
            PendingRequest = null;
            req.CompletionSource.TrySetResult(false);
            OnProductRejected?.Invoke(req.Product);
        }
    }

    /// <summary>
    /// Cancels the pending approval request without adding to cart.
    /// </summary>
    public void Cancel()
    {
        if (_pendingRequest != null)
        {
            var req = _pendingRequest;
            PendingRequest = null;
            req.CompletionSource.TrySetResult(false);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
