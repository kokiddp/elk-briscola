import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { correlationIdInterceptor } from '../../core/correlation-id.interceptor';
import { httpTokenInterceptor } from '../../core/http-token.interceptor';
import { RegisterComponent } from './register.component';

async function setup() {
  const r = await render(RegisterComponent, {
    providers: [
      provideHttpClient(withInterceptors([correlationIdInterceptor, httpTokenInterceptor])),
      provideHttpClientTesting(),
      provideRouter([{ path: '**', component: RegisterComponent }]),
    ],
  });
  const ctrl = r.fixture.debugElement.injector.get(HttpTestingController);
  return { ...r, ctrl };
}

describe('RegisterComponent', () => {
  it('disables submit when invalid', async () => {
    await setup();
    const submit = screen.getByTestId('submit') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
  });

  it('surfaces password validation messages on touch', async () => {
    await setup();
    const pw = screen.getByTestId('password');
    fireEvent.input(pw, { target: { value: 'short' } });
    fireEvent.blur(pw);
    expect(await screen.findByText(/minimum 10 characters/i)).toBeInTheDocument();
  });

  it('enables submit and triggers a registration request on valid input', async () => {
    const { ctrl } = await setup();

    fireEvent.input(screen.getByTestId('username'), { target: { value: 'alice' } });
    fireEvent.input(screen.getByTestId('email'), { target: { value: 'alice@example.com' } });
    fireEvent.input(screen.getByTestId('password'), { target: { value: 'hunter2hunter' } });
    fireEvent.input(screen.getByTestId('displayName'), { target: { value: 'Alice' } });

    const submit = screen.getByTestId('submit') as HTMLButtonElement;
    expect(submit.disabled).toBe(false);

    fireEvent.click(submit);
    const req = ctrl.expectOne('/api/v1/auth/register');
    expect(req.request.body).toEqual({
      username: 'alice',
      email: 'alice@example.com',
      password: 'hunter2hunter',
      displayName: 'Alice',
    });

    // We don't drive the post-register login flow in this unit test —
    // just acknowledge the request and stop there.
    req.flush(null, { status: 201, statusText: 'Created' });
    ctrl.verify();
  });
});
