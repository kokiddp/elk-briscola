import { fireEvent, render, screen } from '@testing-library/angular';
import { describe, expect, it, vi } from 'vitest';
import { CreateGameDialogComponent } from './create-game-dialog.component';
import { CreateGameRequest } from './lobby.models';

async function setup(opts: { submitting?: boolean } = {}) {
  const submitted = vi.fn<(req: CreateGameRequest) => void>();
  const cancelled = vi.fn<() => void>();
  const r = await render(CreateGameDialogComponent, {
    inputs: { submitting: opts.submitting ?? false },
    on: {
      submitted: (req: CreateGameRequest) => submitted(req),
      cancelled: () => cancelled(),
    },
  });
  return { ...r, submitted, cancelled };
}

describe('CreateGameDialogComponent', () => {
  it('emits submitted with the mode + privacy on submit (no name collected)', async () => {
    const { submitted } = await setup();
    fireEvent.click(screen.getByTestId('create-submit'));
    expect(submitted).toHaveBeenCalledTimes(1);
    expect(submitted).toHaveBeenCalledWith({
      mode: 'TwoPlayer',
      isPrivate: false,
      password: null,
    });
  });

  it('hides the password field when isPrivate is off and shows it when on', async () => {
    await setup();
    expect(screen.queryByTestId('create-password')).toBeNull();
    fireEvent.click(screen.getByTestId('create-isPrivate'));
    expect(screen.getByTestId('create-password')).toBeInTheDocument();
  });

  it('requires the password when isPrivate is toggled on', async () => {
    await setup();
    fireEvent.click(screen.getByTestId('create-isPrivate'));
    const submit = screen.getByTestId('create-submit') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    fireEvent.input(screen.getByTestId('create-password'), { target: { value: 'abc' } });
    expect(submit.disabled).toBe(true);
    fireEvent.input(screen.getByTestId('create-password'), { target: { value: 'abcd' } });
    expect(submit.disabled).toBe(false);
  });

  it('emits submitted with the password when private', async () => {
    const { submitted } = await setup();
    fireEvent.click(screen.getByTestId('create-isPrivate'));
    fireEvent.input(screen.getByTestId('create-password'), { target: { value: 'hunter2' } });
    fireEvent.click(screen.getByTestId('create-submit'));
    expect(submitted).toHaveBeenCalledWith({
      mode: 'TwoPlayer',
      isPrivate: true,
      password: 'hunter2',
    });
  });

  it('emits cancelled when the backdrop is clicked', async () => {
    const { cancelled } = await setup();
    fireEvent.click(screen.getByTestId('create-backdrop'));
    expect(cancelled).toHaveBeenCalledTimes(1);
  });

  it('emits cancelled when the Cancel button is clicked', async () => {
    const { cancelled } = await setup();
    fireEvent.click(screen.getByTestId('create-cancel'));
    expect(cancelled).toHaveBeenCalledTimes(1);
  });

  it('shows the "submitting" label and disables submit when submitting input is true', async () => {
    await setup({ submitting: true });
    const submit = screen.getByTestId('create-submit') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    expect(submit.textContent ?? '').toMatch(/creating/i);
  });

  it('does not emit cancelled while submitting is true', async () => {
    const { cancelled } = await setup({ submitting: true });
    fireEvent.click(screen.getByTestId('create-backdrop'));
    fireEvent.click(screen.getByTestId('create-cancel'));
    expect(cancelled).not.toHaveBeenCalled();
  });

  it('does not emit submitted while submitting is true', async () => {
    const { submitted } = await setup({ submitting: true });
    fireEvent.click(screen.getByTestId('create-submit'));
    expect(submitted).not.toHaveBeenCalled();
  });
});
