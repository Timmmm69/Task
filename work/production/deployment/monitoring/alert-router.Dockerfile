ARG PYTHON_IMAGE
FROM ${PYTHON_IMAGE}

WORKDIR /app
COPY alert_router.py /app/alert_router.py
RUN mkdir -p /var/lib/task-alert-router \
    && chown 65532:65532 /var/lib/task-alert-router

USER 65532:65532
EXPOSE 8080
ENTRYPOINT ["python", "/app/alert_router.py"]
