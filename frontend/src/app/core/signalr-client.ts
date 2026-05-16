import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from '@microsoft/signalr';

export function createHubConnection(
  path: string,
  getAccessToken: () => Promise<string>,
): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(path, { accessTokenFactory: getAccessToken })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Information)
    .build();
}
