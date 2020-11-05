# Description

Azure HTTP function app that provides data related to Ferguson items, such as descriptions, weights, preferred shipping methods, stocking statuses, vendors, etc.


# OpenAPI Definition




# CI/CD workflow and Pull Request instructions

1. Create a new branch with a name suitable for the code being added/refactored.

2. Send initial pull request to the **test** branch. Once merged, a build & deploy action in **Debug** configuration will be triggered to the _item-microservices-test_ function app. OpenAPI defintion here: 

3. Once approved in test, the next pull request should be sent to the **staging** branch. Once merged, a build & deploy action in **Release** configuration will be triggered to the _item-microservices_ staging environment (a deployment slot in the production function app). OpenAPI defintion here: 

4. Once approved in staging, the final pull request should be sent to the **master** branch. Once merged, a build & deploy action in **Release** configuration will be triggered to the _item-microservices_ production environment. OpenAPI defintion in section above.
