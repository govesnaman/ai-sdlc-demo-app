import { expect, test } from '@playwright/test';
import { InvoiceApprovalPage } from '../pages/invoice-approval-page';

test('Manager approval above the limit is rejected without changing invoice state', async ({ page }) => {
  const approvalPage = new InvoiceApprovalPage(page);
  const customerName = `Manager limit test ${Date.now()}`;

  await page.goto('/');
  await approvalPage.signIn('demo.manager', 'ChangeMe123!');
  const invoiceId = await approvalPage.createPendingInvoice(customerName, 75_000);
  await approvalPage.openApprovals();
  await approvalPage.attemptApproval(invoiceId);

  await approvalPage.expectApprovalLimitError();
  await approvalPage.expectInvoiceStillPending(invoiceId);

  const persistedInvoice = await approvalPage.getPersistedInvoice(invoiceId);
  expect(persistedInvoice.status).toBe('PendingApproval');
  expect(persistedInvoice.approvals).toEqual([]);
});
