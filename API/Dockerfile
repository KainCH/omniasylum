# Use Node.js 20 LTS Alpine for smaller image
FROM node:20-alpine

# Set working directory
WORKDIR /app

# Copy package files
COPY package*.json ./

# Install production dependencies only
RUN npm ci --only=production && npm cache clean --force

# Copy application files
COPY *.js ./

# Create data directory
RUN mkdir -p /app/data

# Set environment to production
ENV NODE_ENV=production

# Expose port (Azure Container Apps will override this)
EXPOSE 3000

# Use non-root user for security
USER node

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD node -e "require('http').get('http://localhost:3000/api/health', (r) => {process.exit(r.statusCode === 200 ? 0 : 1)})"

# Start application
CMD ["node", "server.js"]
