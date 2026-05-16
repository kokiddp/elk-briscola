import { signal } from '@angular/core';
import { fireEvent, render, screen } from '@testing-library/angular';
import { describe, expect, it, vi } from 'vitest';
import { LobbyChatPanelComponent } from './lobby-chat-panel.component';
import { LobbyChatMessage } from './lobby.models';
import { LobbyService } from './lobby.service';

function makeLobby(opts: {
  log?: LobbyChatMessage[];
  sendChat?: (text: string) => Promise<void>;
}): { svc: LobbyService; sent: string[] } {
  const sent: string[] = [];
  const logSig = signal<readonly LobbyChatMessage[]>(opts.log ?? []);
  const sendChat =
    opts.sendChat ??
    ((text: string) => {
      sent.push(text);
      return Promise.resolve();
    });
  const svc = {
    chatLog: () => logSig(),
    sendChat,
  } as unknown as LobbyService;
  return { svc, sent };
}

async function setup(
  opts: {
    log?: LobbyChatMessage[];
    sendChat?: (text: string) => Promise<void>;
  } = {},
) {
  const lobby = makeLobby(opts);
  const r = await render(LobbyChatPanelComponent, {
    providers: [{ provide: LobbyService, useValue: lobby.svc }],
  });
  return { ...r, ...lobby };
}

const MSG: LobbyChatMessage = {
  id: 'm1',
  fromUserId: 'u1',
  fromUserName: 'alice',
  text: 'hello',
  createdAt: '2026-05-16T10:00:00Z',
};

describe('LobbyChatPanelComponent', () => {
  it('shows the empty state when there are no messages', async () => {
    await setup();
    expect(screen.getByText(/no messages yet/i)).toBeInTheDocument();
  });

  it('renders messages from the chat log signal', async () => {
    await setup({ log: [MSG] });
    const items = screen.getAllByTestId('chat-message');
    expect(items).toHaveLength(1);
    expect(items[0]).toHaveTextContent('alice');
    expect(items[0]).toHaveTextContent('hello');
  });

  it('disables the send button until text is entered', async () => {
    await setup();
    const send = screen.getByTestId('chat-send') as HTMLButtonElement;
    expect(send.disabled).toBe(true);
    fireEvent.input(screen.getByTestId('chat-input'), { target: { value: 'hi' } });
    expect(send.disabled).toBe(false);
  });

  it('calls sendChat on submit and clears the input', async () => {
    const sendChat = vi.fn((_text: string) => Promise.resolve());
    const { fixture } = await setup({ sendChat });

    const input = screen.getByTestId('chat-input') as HTMLInputElement;
    fireEvent.input(input, { target: { value: 'hi all' } });
    fireEvent.click(screen.getByTestId('chat-send'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(sendChat).toHaveBeenCalledWith('hi all');
    expect(input.value).toBe('');
  });

  it('ignores whitespace-only submissions', async () => {
    const sendChat = vi.fn(() => Promise.resolve());
    await setup({ sendChat });

    fireEvent.input(screen.getByTestId('chat-input'), { target: { value: '   ' } });
    fireEvent.click(screen.getByTestId('chat-send'));

    expect(sendChat).not.toHaveBeenCalled();
  });
});
