import { fireEvent, render, screen } from '@testing-library/angular';
import { beforeEach, describe, expect, it } from 'vitest';
import { ErrorToastService } from '../core/error-toast.service';
import { ToastHostComponent } from './toast-host.component';

describe('ToastHostComponent', () => {
  let service: ErrorToastService;

  beforeEach(() => {
    service = new ErrorToastService();
  });

  it('renders nothing when there are no toasts', async () => {
    await render(ToastHostComponent, {
      providers: [{ provide: ErrorToastService, useValue: service }],
    });
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('renders the live toast list with kind-specific classes', async () => {
    service.error('boom');
    service.success('yay');
    const r = await render(ToastHostComponent, {
      providers: [{ provide: ErrorToastService, useValue: service }],
    });
    r.fixture.detectChanges();

    const items = screen.getAllByRole('status');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('boom');
    expect(items[0]?.classList.contains('error')).toBe(true);
    expect(items[1]).toHaveTextContent('yay');
    expect(items[1]?.classList.contains('success')).toBe(true);
  });

  it('dismisses a toast when its close button is clicked', async () => {
    service.show('info', 'hello', 0);
    const r = await render(ToastHostComponent, {
      providers: [{ provide: ErrorToastService, useValue: service }],
    });
    r.fixture.detectChanges();

    expect(screen.getByRole('status')).toHaveTextContent('hello');
    fireEvent.click(screen.getByLabelText(/dismiss/i));
    r.fixture.detectChanges();
    expect(screen.queryByRole('status')).toBeNull();
  });
});
