using InvoiceApp.Api.Auth;
using InvoiceApp.Api.Controllers;
using InvoiceApp.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalsControllerTests
{
    [Fact]
    public void Approve_LimitExceeded_ReturnsConflictWithCodeAndReason()
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            CustomerName = "Test School",
            Amount = 50_001m,
            Status = InvoiceStatus.PendingApproval
        };
        var repository = new FakeInvoiceRepository(invoice);
        var controller = ControllerFor(new UserSession("manager-1", "Manager"), repository);

        var action = controller.Approve("INV-1");

        var response = Assert.IsType<ConflictObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("APPROVAL_LIMIT_EXCEEDED", Property(response.Value!, "errorCode"));
        Assert.Equal(
            "APPROVAL_LIMIT_EXCEEDED: Manager approval limit is 50,000; invoice amount is 50,001",
            Property(response.Value!, "error"));
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public void Approve_AdminRole_ReturnsConflictWithUnauthorizedRoleReason()
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            CustomerName = "Test School",
            Amount = 49_999m,
            Status = InvoiceStatus.PendingApproval
        };
        var repository = new FakeInvoiceRepository(invoice);
        var controller = ControllerFor(new UserSession("admin-1", "Admin"), repository);

        var action = controller.Approve("INV-1");

        var response = Assert.IsType<ConflictObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("UNAUTHORIZED_ROLE", Property(response.Value!, "errorCode"));
        Assert.Equal(
            "UNAUTHORIZED_ROLE: Role Admin is not authorized to approve invoices",
            Property(response.Value!, "error"));
        Assert.Equal(0, repository.UpdateCount);
    }

    private static ApprovalsController ControllerFor(UserSession session, FakeInvoiceRepository repository)
    {
        var controller = new ApprovalsController(new ApprovalService(repository))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Items[HttpContextExtensions.SessionKey] = session;
        return controller;
    }

    private static string? Property(object value, string name) =>
        value.GetType().GetProperty(name)?.GetValue(value) as string;
}
