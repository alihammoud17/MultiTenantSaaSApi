import { createApp } from './app.ts';
import { logger } from './observability/logger.ts';

const { server, config } = createApp();

server.listen(config.port, () => {
  logger.info('billing-service.started', {
    service: config.serviceName,
    port: config.port,
    provider: config.provider,
    nodeEnv: config.nodeEnv
  });
});
