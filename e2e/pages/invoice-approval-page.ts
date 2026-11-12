import { expect, type Page } from '@playwright/test';

export class InvoiceApprovalPage {
  constructor(private readonly page: Page) {}

  async signIn(username: string, password: string): Promise<void> {
    await this.page.getByLabel('Username').fill(username);
    await this.page.getByLabel('Password').fill(password);
    await this.page.getByRole('button', { name: 'Sign in' }).click();
    await expect(this.page.getByText(`${username} (Manager)`)).toBeVisible();
  }

  async createPendingInvoice(customerName: string, amount: number): Promise<string> {
    await this.page.getByLabel('Customer name').fill(customerName);
    await this.page.getByLabel('Amount').fill(String(amount));
    await this.page.getByRole('button', { name: 'Create invoice' }).click();

    const invoiceRow = this.page.getByRole('row').filter({ hasText: customerName });
    await expect(invoiceRow).toBeVisible();
    const invoiceId = (await invoiceRow.getByRole('cell').first().innerText()).trim();

    await this.page.getByRole('button', { name: `Submit invoice ${invoiceId} for approval` }).click();
    await expect(invoiceRow.getByRole('cell').nth(3)).toHaveText('Pending Approval');
    return invoiceId;
  }

  async openApprovals(): Promise<void> {
    await this.page.getByRole('tab', { name: 'Approvals' }).click();
  }

  async attemptApproval(invoiceId: string): Promise<void> {
    await this.page.getByRole('button', { name: `Approve invoice ${invoiceId}` }).click();
  }

  async expectApprovalLimitError(): Promise<void> {
    await expect(
      this.page.getByRole('alert').filter({ hasText: 'APPROVAL_LIMIT_EXCEEDED' })
    ).toContainText('Manager approval limit is 50,000');
  }

  async expectInvoiceStillPending(invoiceId: string): Promise<void> {
    await this.page.getByRole('tab', { name: 'Invoices' }).click();
    const invoiceRow = this.page.getByRole('row').filter({ hasText: invoiceId });
    await expect(invoiceRow.getByRole('cell').nth(3)).toHaveText('Pending Approval');
    await this.page.getByRole('tab', { name: 'Approvals' }).click();
    await expect(
      this.page.getByRole('button', { name: `Approve invoice ${invoiceId}` })
    ).toBeVisible();
  }

  async getPersistedInvoice(invoiceId: string): Promise<{ status: string; approvals: unknown[] }> {
    return this.page.evaluate(async (id) => {
      const sessionValue = sessionStorage.getItem('session');
      if (!sessionValue) {
        throw new Error('Expected an authenticated session while checking invoice state.');
      }

      const session = JSON.parse(sessionValue) as { token: string };
      const response = await fetch(`/api/invoices/${encodeURIComponent(id)}`, {
        headers: { Authorization: `Bearer ${session.token}` }
      });
      if (!response.ok) {
        throw new Error(`Invoice state request failed with status ${response.status}.`);
      }
      return response.json() as Promise<{ status: string; approvals: unknown[] }>;
    }, invoiceId);
  }
}
