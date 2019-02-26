pipeline {
  agent { 
      label 'low-side'
  }

  environment {
      M3T4C0_REGISTRY     = credentials('metaco-registry')
      DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE = credentials('DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE')
      DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE = ('DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE_NBXPLORER')
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
            env
            docker login -u ${M3T4C0_REGISTRY_USR} -p ${M3T4C0_REGISTRY_PSW} registry.m3t4c0.com
            docker build -t registry.m3t4c0.com/silo-dev/nbxplorer:${DOCKER_TAG} -f Dockerfile.linuxamd64 .
            docker push registry.m3t4c0.com/silo-dev/nbxplorer:${DOCKER_TAG}
            '''
      }
    }

    stage('Deploy Docker Sign Sign') {
      when { tag "r*" }
      steps {
          sh '''
            docker login -u ${M3T4C0_REGISTRY_USR} -p ${M3T4C0_REGISTRY_PSW} registry.metaco.network
            docker build -t registry.metaco.network/silo/nbxplorer:${TAG_NAME} -f Dockerfile.linuxamd64 .
            docker run --rm \
            -v /var/run/docker.sock:/var/run/docker.sock \
            -e DOCKER_CONTENT_TRUST=1 \
            -e DOCKER_CONTENT_TRUST_SERVER='https://registry.metaco.network:4443' \
            -e DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE="${DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE}" \
            -e DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE="${DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE}" \
            registry.m3t4c0.com/devops/docker:signer docker push registry.metaco.network/silo/nbxplorer:${TAG_NAME}
            '''
      }
    }
  }
}
