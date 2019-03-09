pipeline {
  agent { 
      label 'low-side'
  }

  environment {
      M3T4C0_REGISTRY     = credentials('metaco-registry')
      DOCKER_REGISTRY = 'registry.internal.m3t4c0.com'
      DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE = credentials('DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE')
      DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE = ('DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE_NBXPLORER')
        DOCKER_CONTENT_TRUST_REPOSITORY = 'registry.metaco.network/silo/nbxplorer'
        DOCKER_SIGNER_IMG = 'registry.internal.m3t4c0.com/devops/docker:signer'
        DOCKER_CONTENT_TRUST_SERVER = 'https://registry.metaco.network:4443'
        DOCKER_IMG = 'registry.internal.m3t4c0.com/silo/nbxplorer'
  }

  parameters {
    booleanParam(name: 'UNIT_TESTS', defaultValue: true, description: 'Shall run unit tests??')
    booleanParam(name: 'DOCKER_BUILD', defaultValue: false, description: 'Shall build the docker images??')
  }

  options {
    buildDiscarder(logRotator(numToKeepStr:'10'))
    // timeout(time: 5, unit: 'MINUTES')
    ansiColor('xterm')
  }
  stages {
    stage('Deploy Docker image') {
      when {
        anyOf {
          expression { BRANCH_NAME ==~ /(master|development)/ }
          expression { params.DOCKER_BUILD == true }
        }
      }
      steps {
          script {
            env.DOCKER_TAG = env.BRANCH_NAME.replace("/","-")
          }
          sh '''
            docker login -u ${M3T4C0_REGISTRY_USR} -p ${M3T4C0_REGISTRY_PSW} ${DOCKER_REGISTRY}
            docker build -t ${DOCKER_IMG}:${BRANCH_NAME} -f Dockerfile.linuxamd64 .
            docker push ${DOCKER_IMG}:${BRANCH_NAME}
            '''
      }
    }

    stage('Deploy Docker Sign Sign') {
      when { tag 'r[0-9]+\\.[0-9]+\\.[0-9]+$' }

      steps {
          sh '''
              docker login -u ${M3T4C0_REGISTRY_USR} -p ${M3T4C0_REGISTRY_PSW} ${DOCKER_CONTENT_TRUST_REGISTRY}
              docker build -t ${DOCKER_BUILDER_IMG}:${TAG_NAME} -f Dockerfile.linuxamd64 .
              docker run --rm \
              -v /var/run/docker.sock:/var/run/docker.sock \
              -e DOCKER_CONTENT_TRUST=1 \
              -e DOCKER_CONTENT_TRUST_SERVER="${DOCKER_CONTENT_TRUST_SERVER}" \
              -e DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE="${DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE}" \
              -e DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE="${DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE}" \
              ${DOCKER_SIGNER_IMG} docker push ${DOCKER_CONTENT_TRUST_REPOSITORY}:${TAG_NAME}
            '''
      }
    }
  }
}
