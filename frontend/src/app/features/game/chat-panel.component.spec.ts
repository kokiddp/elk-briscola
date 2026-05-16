import { fireEvent, render, screen } from '@testing-library/angular';
import { describe, expect, it, vi } from 'vitest';
import { ChatPanelComponent } from './chat-panel.component';
import { GameChatMessage } from './game.models';

const MSG: GameChatMessage = {
  id: 'm1',
  gameId: 'g1',
  fromUserId: 'u1',
  fromUserName: 'alice',
  text: 'hi',
  createdAt: '2026-05-17T10:00:00Z',
};

async function setup(
  opts: {
    messages?: GameChatMessage[];
    tacticalBanner?: boolean;
    canSend?: boolean;
  } = {},
) {
  const onSend = vi.fn<(text: string) => void>();
  const r = await render(ChatPanelComponent, {
    inputs: {
      messages: opts.messages ?? [],
      tacticalBanner: opts.tacticalBanner ?? false,
      canSend: opts.canSend ?? true,
    },
    on: { send: (text: string) => onSend(text) },
  });
  return { ...r, onSend };
}

describe('ChatPanelComponent', () => {
  it('shows the empty state when there are no messages', async () => {
    await setup();
    expect(screen.getByText(/no messages/i)).toBeInTheDocument();
  });

  it('renders messages', async () => {
    await setup({ messages: [MSG] });
    expect(screen.getByTestId('chat-message')).toHaveTextContent('hi');
  });

  it('shows the tactical banner in 4p LastHand', async () => {
    await setup({ tacticalBanner: true });
    expect(screen.getByTestId('tactical-banner')).toBeInTheDocument();
  });

  it('emits send with the trimmed text and clears the input', async () => {
    const { fixture, onSend } = await setup();
    const input = screen.getByTestId('chat-input') as HTMLInputElement;
    fireEvent.input(input, { target: { value: '  hello there  ' } });
    fireEvent.click(screen.getByTestId('chat-send'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(onSend).toHaveBeenCalledWith('hello there');
    expect(input.value).toBe('');
  });

  it('disables input + send when canSend is false', async () => {
    await setup({ canSend: false });
    expect((screen.getByTestId('chat-input') as HTMLInputElement).disabled).toBe(true);
    expect((screen.getByTestId('chat-send') as HTMLButtonElement).disabled).toBe(true);
  });

  it('formats timestamps and falls back for invalid input', async () => {
    const { fixture } = await setup();
    const cmp = fixture.componentInstance;
    expect(cmp.formatTimestamp('2026-05-17T10:00:00Z')).toMatch(/\d{1,2}:\d{2}/);
    expect(cmp.formatTimestamp('not-a-date')).toBe('');
  });
});
