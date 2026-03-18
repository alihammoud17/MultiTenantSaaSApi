import { createApp } from './app.ts';

const { server, config } = createApp();

server.listen(config.port, () => {
  console.info('billing-service.started', {
    port: config.port,
    provider: config.provider,
    nodeEnv: config.nodeEnv
  });
});
